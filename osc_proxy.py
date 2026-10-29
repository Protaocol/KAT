#!/usr/bin/env python3
"""
OSC proxy / sniffer for VRChat, with KAT (paged glyph protocol) decoding.

Listens on (listen_ip, listen_port), prints every message it receives,
decodes it according to katosc.py's wire format when it recognizes the
address, and forwards the raw message unchanged to (forward_ip, forward_port).

Requires: pip install python-osc --break-system-packages

KAT wire format (see kat_codec.py):
  - A synced float carries an 8-bit "symbol" s in 0..254:
        k = round(v * 127)         (k in -127..127)
        s = k if k >= 0 else k + 255
  - /avatar/parameters/KAT_CharSync{i}   -> symbol is the SLOT (0..254) of a glyph
  - /avatar/parameters/KAT_PageSync{i}   -> page format 2: symbol packs a PAIR of pages: pa*15 + pb
                                            page format 3: symbol IS the page of slot i (one page per float)
  - /avatar/parameters/KAT_Pointer       -> int/float write-cursor position (255 = reset)
  - /avatar/parameters/KAT_Visible       -> bool, textbox visibility
  - /avatar/parameters/KAT_Speed         -> float 0..1, rolling speed dial

Glyph index g = page * 255 + slot; page 0 = narrow (ASCII), pages 1.. = wide
(hanzi), drawn across two slots [lead][FOLLOW] with FOLLOW = slot 254 of page 0.

To actually render characters instead of just page/slot numbers, pass
--charset pointing at a charset.json (narrow/wide glyph strings), and this
proxy will pair up CharSync/PageSync groups and print the resulting text.

The page format comes from the charset file (like the client). Without --charset, pass --page-format 2|3, or leave it
out and the proxy guesses from how many PageSync floats accompany the CharSync floats (M/2 => format 2, M => format 3);
guesses are marked with "~" because a burst that is still arriving can look like the wrong format for a moment.
"""

import sys
import argparse
import datetime
import json
import re

from pythonosc import dispatcher, osc_server, udp_client

from kat_codec import read_charset_format
from kat_protocol import (FOLLOW, PAGE_BASE, PAGE_FORMATS, SLOTS_PER_PAGE, SYMBOLS, infer_format_from_param_counts)

PREFIX = "/avatar/parameters/"
WIDE_START = SLOTS_PER_PAGE

CHAR_RE = re.compile(r"^KAT_CharSync(\d+)$")
PAGE_RE = re.compile(r"^KAT_PageSync(\d+)$")


def symbol_to_float_range(v: float) -> int:
    """float (-1..1) -> 8-bit symbol (0..254), per kat_codec.symbol_to_float inverse."""
    k = int(round(v * 127.0))
    return k if k >= 0 else k + SYMBOLS


class Charset:
    """Minimal mirror of kat_codec.Charset, for turning glyph indices back into text."""

    def __init__(self, narrow: str, wide: str, page_format: int = 2, color: list | None = None):
        self.narrow = narrow
        self.wide = wide
        self.page_format = page_format
        self.color = list(color or [])
        # emoji sit on the pages right after the wide text pages
        self.color_first = 1 + -(-len(wide) // 255)

    @classmethod
    def load(cls, path: str) -> "Charset":
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        return cls(d["narrow"], d["wide"], read_charset_format(d), d.get("color"))

    def char_at(self, glyph_index: int) -> str:
        if 0 <= glyph_index < len(self.narrow):
            return self.narrow[glyph_index]
        j = glyph_index - WIDE_START
        if 0 <= j < len(self.wide):
            return self.wide[j]
        k = glyph_index - self.color_first * 255
        if 0 <= k < len(self.color):
            return self.color[k]
        return ""  # FOLLOW marker or unassigned cell


class KatDecoder:
    """Buffers CharSync/PageSync floats across one OSC 'burst' and decodes glyphs."""

    def __init__(self, charset: Charset | None, page_format: int | None = None):
        self.charset = charset
        self.explicit_format = charset.page_format if charset is not None else page_format
        self.chars: dict[int, float] = {}
        self.pages: dict[int, float] = {}

    def page_format(self) -> tuple[int | None, bool]:
        """(format, guessed). Explicit (charset / flag) wins; otherwise infer from the buffered parameter counts."""
        if self.explicit_format is not None:
            return self.explicit_format, False
        if self.chars and self.pages:
            return infer_format_from_param_counts(max(self.chars) + 1, max(self.pages) + 1), True
        return None, True

    def feed_char(self, i: int, v: float):
        self.chars[i] = v

    def feed_page(self, i: int, v: float):
        self.pages[i] = v

    def decode_symbol(self, v: float) -> int:
        return symbol_to_float_range(v)

    def decode_available(self) -> str:
        """Decode however many (char, page-pair) slots are currently buffered."""
        if not self.chars:
            return ""
        glyphs = []
        n = max(self.chars) + 1
        fmt, guessed = self.page_format()
        for i in range(n):
            if i not in self.chars:
                glyphs.append(None)
                continue
            slot = self.decode_symbol(self.chars[i])
            if fmt is None:
                glyphs.append(None)
                continue
            if fmt == 3:
                if i not in self.pages:
                    glyphs.append(None)
                    continue
                page = self.decode_symbol(self.pages[i])
            else:
                pair_idx = i // 2
                if pair_idx not in self.pages:
                    glyphs.append(None)
                    continue
                pair = self.decode_symbol(self.pages[pair_idx])
                if pair >= PAGE_BASE * PAGE_BASE:
                    glyphs.append(None)                 # not a valid format-2 pair: probably format-3 data
                    continue
                page = pair // PAGE_BASE if i % 2 == 0 else pair % PAGE_BASE
            glyphs.append(page * SLOTS_PER_PAGE + slot)

        parts = []
        for g in glyphs:
            if g is None:
                parts.append("?")
            elif g % SLOTS_PER_PAGE == FOLLOW and (g // SLOTS_PER_PAGE) == 0:
                continue  # right half of previous wide glyph, no separate char
            elif self.charset is not None:
                ch = self.charset.char_at(g)
                parts.append(ch if ch else f"<g{g}>")
            else:
                page, slot = divmod(g, SLOTS_PER_PAGE)
                parts.append(f"(p{page}:s{slot})")
        return "".join(parts)


def decode_message(address: str, args, decoder: KatDecoder):
    """Return a human-readable decoding of this message, or None if not KAT-related."""
    if not address.startswith(PREFIX):
        return None
    name = address[len(PREFIX):]

    m = CHAR_RE.match(name)
    if m and args:
        i = int(m.group(1))
        v = float(args[0])
        decoder.feed_char(i, v)
        symbol = decoder.decode_symbol(v)
        text = decoder.decode_available()
        return f"CharSync[{i}] = {v:+.4f} -> slot {symbol}   | buffered text: {text!r}"

    m = PAGE_RE.match(name)
    if m and args:
        i = int(m.group(1))
        v = float(args[0])
        decoder.feed_page(i, v)
        symbol = decoder.decode_symbol(v)
        fmt, guessed = decoder.page_format()
        mark = "~" if guessed else ""
        text = decoder.decode_available()
        if fmt == 3:
            what = f"page {symbol} for slot {i}"
        elif fmt == 2:
            what = (f"pages ({symbol // PAGE_BASE}, {symbol % PAGE_BASE})" if symbol < PAGE_BASE * PAGE_BASE
                    else f"symbol {symbol} (not a valid format-2 pair)")
        else:
            what = f"symbol {symbol} (page format unknown yet)"
        return f"PageSync[{i}] = {v:+.4f} -> {what}  [fmt {mark}{fmt}]  | buffered text: {text!r}"

    if name == "KAT_Pointer" and args:
        v = args[0]
        # KAT_Pointer is an Int: 1..N = "write group v", 255 = clear. Treat it as an integer so equivalent
        # float/string representations cannot trigger reset handling accidentally.
        return f"Pointer = {v!r}" + ("  (RESET)" if v == 255 else "")

    if name == "KAT_Visible" and args:
        return f"Visible = {bool(args[0])}"

    if name == "KAT_Speed" and args:
        return f"Speed = {args[0]:.4f}"

    return None


def make_handler(client: udp_client.SimpleUDPClient, decoder: KatDecoder, quiet: bool):
    def handler(address: str, *args):
        if not quiet:
            ts = datetime.datetime.now().strftime("%H:%M:%S.%f")[:-3]
            raw = ", ".join(f"{a!r}" for a in args)
            print(f"[{ts}] {address}  raw=[{raw}]")
            kat = decode_message(address, args, decoder)
            if kat:
                print(f"           -> KAT: {kat}")
        client.send_message(address, args if len(args) != 1 else args[0])
    return handler


def main():
    parser = argparse.ArgumentParser(description="OSC proxy: logs, KAT-decodes, and forwards messages.")
    parser.add_argument("--listen-ip", default="127.0.0.1")
    parser.add_argument("--listen-port", type=int, default=9001,
                         help="Port this proxy listens on (default 9001)")
    parser.add_argument("--forward-ip", default="127.0.0.1")
    parser.add_argument("--forward-port", type=int, default=9000,
                         help="Port to forward decoded messages to (default 9000)")
    parser.add_argument("--charset", default=None,
                         help="Path to a charset.json (narrow/wide fields) to render actual glyphs")
    parser.add_argument("--page-format", type=int, choices=sorted(PAGE_FORMATS), default=None,
                         help="Page wire format when there is no --charset (2 or 3). Default: guess from PageSync count")
    parser.add_argument("--quiet", action="store_true", help="Don't print, just forward")
    args = parser.parse_args()

    charset = Charset.load(args.charset) if args.charset else None
    if charset is not None and args.page_format is not None and args.page_format != charset.page_format:
        parser.error(f"--page-format {args.page_format} contradicts {args.charset} (format {charset.page_format})")
    decoder = KatDecoder(charset, args.page_format)

    client = udp_client.SimpleUDPClient(args.forward_ip, args.forward_port)

    disp = dispatcher.Dispatcher()
    disp.set_default_handler(make_handler(client, decoder, args.quiet))

    server = osc_server.ThreadingOSCUDPServer((args.listen_ip, args.listen_port), disp)

    print(f"OSC proxy listening on {args.listen_ip}:{args.listen_port}, "
          f"forwarding to {args.forward_ip}:{args.forward_port}")
    if charset:
        print(f"KAT charset loaded from {args.charset} (page format {charset.page_format})")
    print("Press Ctrl+C to stop.")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")


if __name__ == "__main__":
    main()
