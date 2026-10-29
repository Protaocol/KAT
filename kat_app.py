"""Tk-free logic of the desktop app (KatOscApp.pyw): what happens when the text changes, how the box is kept inside the
128-slot limit, keep-time and charset handling, settings. Keeping it out of the window code makes all of it testable
without a display.
"""
from __future__ import annotations

import json
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from kat_codec import Charset
from katosc import KEEP_TIME_HELP, KatOsc, parse_keep_time, parse_page_format, parse_sync_params

SETTINGS_KEYS = ("charset", "page_format", "sync_params", "keep_time")


# --------------------------------------------------------------------------------------------------- settings

def settings_path() -> Path:
    """Per-user settings file. Never inside the current directory."""
    if os.name == "nt":
        base = Path(os.environ.get("APPDATA") or Path.home() / "AppData" / "Roaming")
    else:
        base = Path(os.environ.get("XDG_CONFIG_HOME") or Path.home() / ".config")
    return base / "KatOscApp" / "settings.json"


def load_settings(path: Path | None = None) -> dict:
    try:
        data = json.loads((path or settings_path()).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return {k: v for k, v in data.items() if k in SETTINGS_KEYS and isinstance(v, str)} if isinstance(data, dict) else {}


def save_settings(settings: dict, path: Path | None = None) -> None:
    path = path or settings_path()
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({k: settings[k] for k in SETTINGS_KEYS if k in settings}, ensure_ascii=False, indent=1),
                        encoding="utf-8")
    except OSError as exc:                       # a read-only profile folder must not stop the app
        print(f"KAT: could not save settings to {path}: {exc}", file=sys.stderr)


def apply_saved_defaults(args, saved: dict):
    """Fill options the command line left out with the last-used values. An explicit command-line value always wins
    (including an explicitly empty --keep-time, which means 'never clear'). A saved value that no longer parses is
    ignored rather than stopping the app from starting."""
    validators = {"page_format": parse_page_format, "sync_params": parse_sync_params, "keep_time": parse_keep_time}
    for key, validate in validators.items():
        if getattr(args, key, None) is None and key in saved:
            try:
                validate(saved[key])
            except ValueError:
                continue
            setattr(args, key, saved[key])
    if getattr(args, "charset", None) is None and saved.get("charset"):
        args.saved_charset = saved["charset"]
    return args


# --------------------------------------------------------------------------------------------------- charset

def load_charset(path: str | os.PathLike) -> tuple[Charset, str]:
    """Load a charset file; every failure is a ValueError that names the file."""
    p = Path(path).expanduser()
    try:
        return Charset.load(str(p)), str(p.resolve())
    except FileNotFoundError:
        raise ValueError(f"Charset file not found: {p}") from None
    except KeyError as exc:
        raise ValueError(f"{p.name} is not a KAT charset (it has no {exc.args[0]!r} field)") from None
    except (OSError, ValueError, TypeError) as exc:
        raise ValueError(f"{p.name} is not a usable KAT charset: {exc}") from None


def pick_charset(cli_path, saved_path, ask: Callable[[], str], report: Callable[[str], None]):
    """Decide which charset to start with: --charset (an error if it is bad), else the last one used (skipped quietly if
    it has gone), else ask the user until a file loads or they cancel. Returns (Charset, path) or None on cancel.
    The current directory is never searched."""
    if cli_path:
        return load_charset(cli_path)
    if saved_path:
        try:
            return load_charset(saved_path)
        except ValueError:
            pass
    while True:
        chosen = ask()
        if not chosen:
            return None
        try:
            return load_charset(chosen)
        except ValueError as exc:
            report(str(exc))


# --------------------------------------------------------------------------------------------------- controller

@dataclass
class TextUpdate:
    text: str                 # what the box must contain
    cursor: int               # cursor position, in characters
    rewrite: bool             # True: the widget must be rewritten with ``text`` (it held something that did not fit)
    slots: int                # layout slots used (of ``capacity``)
    capacity: int


@dataclass
class Poll:
    clear_box: bool = False
    alert: str | None = None


class AppController:
    def __init__(self, kat: KatOsc, settings: dict | None = None, converter=None, settings_file: Path | None = None):
        self.kat = kat
        self.settings = dict(settings or {})
        self.converter = converter
        self._settings_path = settings_file
        self._last_text = ""
        self._keep_error: str | None = None
        self._seen_expired = kat.expired_count
        self._alerted: str | None = None
        self.undrawable: list[str] = []

    # ------------------------------------------------------------------ text

    def prepare(self, text: str) -> str:
        """Traditional -> simplified, but ONLY for characters the charset cannot draw as typed (a charset that has
        traditional glyphs keeps them)."""
        if not self.converter or not text:
            return text
        converted = self.converter.convert(text)
        if len(converted) != len(text):                        # phrase-level conversion changed the length: go per character
            converted = "".join(self.converter.convert(ch) for ch in text)
            if len(converted) != len(text):
                return text
        cs = self.kat.charset
        return "".join(o if cs.contains(o) else c for o, c in zip(text, converted))

    # A shortcode like :some_long_emoji_name: is typed in full before it shrinks to one glyph, so the box has to take a little more than it can show while
    # one is being typed. That is allowed only when a ':' sits between the 2nd-last and the 34th-last character (an opening colon close enough for the
    # name to still be coming). Anything else over the limit is refused as before, and the layout cuts what doesn't fit, so nothing extra is ever drawn.
    OVERFLOW = 32

    @classmethod
    def may_overflow(cls, text: str) -> bool:
        return ":" in text[-(cls.OVERFLOW + 2):]

    def _fits(self, text: str) -> bool:
        used = self.kat.measure_text_slots(text)
        cap = self.kat.text_length
        return used <= cap or (used <= cap + self.OVERFLOW and self.may_overflow(text))

    def fit(self, text: str, cursor: int | None = None) -> TextUpdate:
        """Cut ``text`` to what fits in the textbox. If something was just inserted (compared with the previous text),
        the overflowing part of the INSERTION is dropped, so typing into a full box is refused instead of silently
        eating the end of the message."""
        kat = self.kat
        cap = kat.text_length
        cursor = len(text) if cursor is None else max(0, min(cursor, len(text)))
        if self._fits(text):
            return TextUpdate(text, cursor, False, kat.measure_text_slots(text, padded=False), cap)

        prev = self._last_text
        a = 0
        while a < min(len(prev), len(text)) and prev[a] == text[a]:
            a += 1
        b = 0
        while b < min(len(prev), len(text)) - a and prev[len(prev) - 1 - b] == text[len(text) - 1 - b]:
            b += 1
        head, inserted, tail = text[:a], text[a:len(text) - b], text[len(text) - b:]

        def fits(keep: int) -> bool:
            return self._fits(head + inserted[:keep] + tail)

        if fits(0):
            lo, hi = 0, len(inserted)                          # largest prefix of the insertion that still fits
            while lo < hi:
                mid = (lo + hi + 1) // 2
                lo, hi = (mid, hi) if fits(mid) else (lo, mid - 1)
            result, cursor = head + inserted[:lo] + tail, a + lo
        else:                                                   # the old text itself no longer fits (charset / sync changed)
            lo, hi = 0, len(text)
            while lo < hi:
                mid = (lo + hi + 1) // 2
                lo, hi = (mid, hi) if self._fits(text[:mid]) else (lo, mid - 1)
            result, cursor = text[:lo], min(cursor, lo)
        return TextUpdate(result, cursor, True, kat.measure_text_slots(result, padded=False), cap)

    def _push(self, text: str) -> None:
        prepared = self.prepare(text)
        self.undrawable = self.kat.undrawable_chars(prepared)
        self.kat.set_text(prepared)

    def on_text_changed(self, text: str, cursor: int | None = None) -> TextUpdate:
        update = self.fit(text, cursor)
        self._last_text = update.text
        self._push(update.text)
        return update

    def refresh(self) -> TextUpdate:
        """Re-evaluate the current text (the charset or the sync size changed what fits / what is drawable)."""
        return self.on_text_changed(self._last_text)

    def send(self, text: str) -> TextUpdate:
        """The Send button: rewrite the whole avatar text, even groups that already look right."""
        update = self.on_text_changed(text)
        self.kat.resend()
        return update

    def clear(self) -> None:
        self._last_text = ""
        self.undrawable = []
        self.kat.clear()

    # ------------------------------------------------------------------ keep time

    def apply_keep_time(self, field: str) -> str | None:
        """Apply the keep-time entry. Returns an error message (and changes nothing) for input that is not a number."""
        try:
            value = parse_keep_time(field)
        except ValueError as exc:
            self._keep_error = str(exc)
            return self._keep_error
        self._keep_error = None
        self.kat.set_keep_time(value)
        self.settings["keep_time"] = "" if value is None else field.strip()
        return None

    # ------------------------------------------------------------------ sync / format / charset

    def set_sync_choice(self, choice: str) -> None:
        self.kat.set_sync_params(parse_sync_params(choice))
        self.settings["sync_params"] = str(choice)
        self.refresh()

    def set_format_choice(self, choice: str) -> None:
        self.kat.set_declared_page_format(parse_page_format(choice))
        self.settings["page_format"] = str(choice)

    def switch_charset(self, path: str) -> Charset:
        """Load and switch to another charset; on failure the current one stays and a ValueError names the problem."""
        charset, resolved = load_charset(path)
        self.kat.set_charset(charset, resolved)
        self.settings["charset"] = resolved
        self.save()
        self.refresh()
        return charset

    def save(self) -> None:
        save_settings(self.settings, self._settings_path)

    # ------------------------------------------------------------------ periodic

    def poll(self) -> Poll:
        """Called a few times a second by the window."""
        kat = self.kat
        out = Poll()
        if kat.expired_count != self._seen_expired:
            self._seen_expired = kat.expired_count
            # The keep time already cleared the avatar. Empty the box too -- unless the user has typed again since.
            out.clear_box = kat.target_text == ""
            if out.clear_box:
                self._last_text = ""
                self.undrawable = []
        if kat.format_error and kat.format_error != self._alerted:
            self._alerted = kat.format_error
            out.alert = kat.format_error
        elif not kat.format_error:
            self._alerted = None
        return out

    def status(self) -> tuple[str, bool]:
        """(one line for the status bar, is_error)."""
        kat = self.kat
        if self._keep_error:
            return self._keep_error, True
        if kat.last_error:
            return kat.last_error, True
        if kat.send_error:
            return kat.send_error, True
        parts = []
        if kat.osc_enable_server:
            parts.append("auto-detecting avatar..." if kat.detect_step > 0 else f"sync {kat.sync_params} (detected)")
        else:
            parts.append(f"sync {kat.sync_params} (manual)")
        parts.append(kat.format_status())
        if kat.detect_note:
            parts.append(kat.detect_note)
        if kat.keep_seconds == 0:
            parts.append("KAT hidden (keep time 0)")
        else:
            left = kat.seconds_until_clear()
            if left is not None:
                parts.append(f"clears in {left:.0f}s" if left >= 9.5 else f"clears in {left:.1f}s")
        if self.undrawable:
            shown = "".join(self.undrawable[:8]) + ("..." if len(self.undrawable) > 8 else "")
            parts.append(f"not in this charset, shown as '?': {shown}")
        return " | ".join(parts), False
