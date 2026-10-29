# Avatar Text OSC App for VRChat - paged glyph protocol (KAT v2)
# Derived from KatOsc, Copyright (C) 2022 KillFrenzy / Evan Tran
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program. If not, see <https://www.gnu.org/licenses/>.

import argparse
import math
import threading
import time
import traceback
from threading import Timer

from pythonosc import dispatcher, osc_server, udp_client

from kat_codec import (BLANK, FOLLOW, Charset, KatFormatMismatch, encode_group, max_glyph_index, page_param_count,
                       symbol_to_float)
from kat_protocol import (LINE_COUNT, LINE_LENGTH, PAGE_FORMATS, POINTER_CLEAR, SYNC_PARAMS_DEFAULT, SYNC_PARAMS_MAX,
                          SYNC_PARAMS_MIN, TEXT_LENGTH, check_format, infer_format_from_param_counts)

UNKNOWN = -1                 # shadow value for "we do not know what this slot shows" (forces a rewrite)
CHATBOX_LIMIT = 144          # VRChat's chatbox text limit

KEEP_TIME_HELP = "Keep text time must be a number of seconds (0 = hide KAT, empty = never clear)"


# ------------------------------------------------------------------------------------------------ option parsing

def parse_keep_time(text) -> float | None:
    """'' / None -> None (never clear), '0' -> 0.0 (KAT hidden), '2.5' or '2,5' -> 2.5. Anything else: ValueError."""
    if text is None:
        return None
    if isinstance(text, (int, float)) and not isinstance(text, bool):
        value = float(text)
    else:
        s = str(text).strip().replace(",", ".")
        if not s:
            return None
        try:
            value = float(s)
        except ValueError:
            raise ValueError(KEEP_TIME_HELP) from None
    if not math.isfinite(value) or value < 0:
        raise ValueError(KEEP_TIME_HELP)
    return value


def valid_sync_sizes() -> list[int]:
    """Even sync sizes in the protocol range that divide the text slots (the only ones a stock avatar supports)."""
    return [m for m in range(SYNC_PARAMS_MIN, SYNC_PARAMS_MAX + 1, 2) if TEXT_LENGTH % m == 0]


def parse_sync_params(text) -> int:
    """'auto' / '' / None / '0' -> 0 (auto-detect). Otherwise an even number in the protocol's range
    that divides the text slots (a stock avatar only supports 2, 4, 8 and 16)."""
    s = "" if text is None else str(text).strip().lower()
    if s in ("", "auto", "0"):
        return 0
    try:
        value = int(s)
    except ValueError:
        raise ValueError(f"Sync parameters must be 'auto' or an even number {SYNC_PARAMS_MIN}..{SYNC_PARAMS_MAX}") from None
    if value < SYNC_PARAMS_MIN or value > SYNC_PARAMS_MAX or value % 2:
        raise ValueError(f"Sync parameters must be 'auto' or an even number {SYNC_PARAMS_MIN}..{SYNC_PARAMS_MAX}")
    if TEXT_LENGTH % value:
        raise ValueError(f"Sync parameters must divide the {TEXT_LENGTH} text slots: one of {valid_sync_sizes()}")
    return value


def parse_page_format(text) -> int | None:
    """'auto' / '' / None -> None (detect from the avatar, else follow the charset). Otherwise a known page format."""
    s = "" if text is None else str(text).strip().lower()
    if s in ("", "auto"):
        return None
    try:
        return check_format(int(s))
    except ValueError:
        raise ValueError(f"Page format must be 'auto' or one of {sorted(PAGE_FORMATS)}") from None


def add_cli_arguments(parser: argparse.ArgumentParser) -> None:
    """Every option is optional and independent. Nothing is read from the current directory implicitly."""
    parser.add_argument("--charset", metavar="FILE",
                        help="charset.json to use (switches glyph set). Without it the app reuses your last choice or asks.")
    parser.add_argument("--page-format", metavar="2|3|auto",
                        help="declare the avatar's page format. Normally unnecessary: it is detected from the avatar. "
                             "Default: auto (detected, else the charset's own format)")
    parser.add_argument("--sync-params", metavar="N|auto",
                        help=f"KAT_CharSync count of the avatar (even, {SYNC_PARAMS_MIN}..{SYNC_PARAMS_MAX}). "
                             "Default: auto-detect over OSC")
    parser.add_argument("--keep-time", metavar="SECONDS",
                        help="empty/omitted = never clear; 0 = keep KAT hidden; a positive number = clear the text "
                             "that many seconds after it has been delivered")
    parser.add_argument("--chatbox", action="store_true", default=None,
                        help="also mirror the text to the VRChat chatbox (off by default)")
    parser.add_argument("--osc-ip", default=None, help="VRChat OSC address (default 127.0.0.1)")
    parser.add_argument("--osc-port", type=int, default=None, help="VRChat OSC input port (default 9000)")
    parser.add_argument("--listen-port", type=int, default=None, help="port VRChat sends parameters to (default 9001)")


class KatOscConfig:
    def __init__(self):
        self.osc_ip = "127.0.0.1"
        self.osc_port = 9000                # VRChat OSC input
        self.osc_enable_server = True       # listen on 9001 to auto-detect sync param count and page format
        self.osc_server_ip = "127.0.0.1"
        self.osc_server_port = 9001
        self.osc_delay = 0.25               # seconds between pointer writes (until KAT_Speed says otherwise)
        # Rolling speed dial: KAT_Speed is read back over OSC and mapped exponentially between the configured slow/fast delays.
        self.speed_param = "KAT_Speed"
        self.osc_delay_slow = 0.6
        self.osc_delay_fast = 0.1
        # Idle behaviour. After the text is delivered, a heal pass can periodically resend groups that hold text so clients
        # that join late or miss a write can converge without continuously refreshing blank groups.
        self.resync_interval = 20.0
        self.resync_first_delay = 3.0
        self.sync_params = SYNC_PARAMS_DEFAULT   # KAT_CharSync count, MUST be even. Auto-detected if server enabled
        self.line_length = LINE_LENGTH             # slots per row (a hanzi takes two)
        self.line_count = LINE_COUNT
        self.text_length = TEXT_LENGTH             # total slots
        self.osc_enable_chatbox = False            # mirror the text to the VRChat chatbox (opt-in)
        self.osc_chatbox_delay = 1.25
        # Keep text time: None = never clear automatically, 0 = KAT stays hidden, > 0 = clear n seconds after delivery.
        self.keep_seconds: float | None = None
        # Where the glyph set comes from. There is no default location: pass a path (or a Charset to KatOsc).
        self.charset_path: str | None = None
        # Optional: the page format the avatar was built for. Only matters without OSC readback (manual sync size);
        # with auto-detection the avatar's real layout wins. None = detect, else follow the charset.
        self.avatar_page_format: int | None = None

    @classmethod
    def from_args(cls, args: argparse.Namespace) -> "KatOscConfig":
        """Build a config from the namespace produced by ``add_cli_arguments`` (missing attributes keep defaults)."""
        cfg = cls()
        get = lambda name: getattr(args, name, None)
        cfg.charset_path = get("charset")
        cfg.avatar_page_format = parse_page_format(get("page_format"))
        sync = parse_sync_params(get("sync_params"))
        if sync:
            cfg.sync_params = sync
            cfg.osc_enable_server = False           # a manual size has nothing to detect
        cfg.keep_seconds = parse_keep_time(get("keep_time"))
        if get("chatbox"):
            cfg.osc_enable_chatbox = True
        if get("osc_ip"):
            cfg.osc_ip = args.osc_ip
        if get("osc_port"):
            cfg.osc_port = args.osc_port
        if get("listen_port"):
            cfg.osc_server_port = args.listen_port
        return cfg


class KatOsc:
    PREFIX = "/avatar/parameters/"
    AVATAR_CHANGE = "/avatar/change"
    CHATBOX = "/chatbox/input"
    POINTER_CLEAR = POINTER_CLEAR
    SYNC_PARAMS_MIN = SYNC_PARAMS_MIN
    SYNC_PARAMS_MAX = SYNC_PARAMS_MAX
    TEST_SYMBOL = 97

    def __init__(self, config: KatOscConfig | None = None, charset: Charset | None = None,
                 autostart: bool = True):
        config = config or KatOscConfig()
        if charset is None:
            if not config.charset_path:
                raise ValueError("No charset selected: pass a Charset, or set config.charset_path / --charset")
            charset = Charset.load(config.charset_path)
        self.charset = charset
        self.charset_path = config.charset_path
        # The avatar's page layout. ``detected`` comes from OSC readback and always wins; ``declared`` is the user's
        # manual statement (used when nothing can be read back); the charset's own format is the last resort.
        self.declared_page_format: int | None = None
        if config.avatar_page_format is not None:
            self.declared_page_format = check_format(config.avatar_page_format)
        self.detected_page_format: int | None = None
        self.format_error: str | None = None             # set => the avatar's parameters are inconsistent: do not send
        self._format_notice_printed = False
        self._page_params_count = 0                     # highest KAT_PageSync index + 1 echoed during detection

        self.osc_enable_chatbox = config.osc_enable_chatbox
        self.osc_enable_server = config.osc_enable_server
        self.osc_server_ip = config.osc_server_ip
        self.osc_server_port = config.osc_server_port
        self.line_length = config.line_length
        self.line_count = config.line_count
        self.text_length = config.text_length
        self._state_lock = threading.RLock()
        self.last_error: str | None = None              # server / configuration problems
        self.send_error: str | None = None              # the last OSC send failed (VRChat closed?); clears by itself
        self.detect_note: str | None = None             # detection finished without any answer from the avatar
        self.sync_params = self._normalise_sync_params(config.sync_params or SYNC_PARAMS_DEFAULT)
        self.sync_params_last = self.sync_params

        self.param_speed = config.speed_param
        self.osc_delay_slow = config.osc_delay_slow
        self.osc_delay_fast = config.osc_delay_fast
        self.resync_interval = config.resync_interval
        self.resync_first_delay = config.resync_first_delay
        self.speed: float | None = None       # last KAT_Speed value received (None until the avatar reports it)
        self._clock = time.monotonic          # replaced in tests

        self.param_visible = "KAT_Visible"
        self.param_pointer = "KAT_Pointer"
        self.param_char = "KAT_CharSync"
        self.param_page = "KAT_PageSync"

        # Keep text time. The countdown starts once the text has been fully delivered to the avatar.
        self.keep_seconds: float | None = parse_keep_time(config.keep_seconds)
        self.expired_count = 0                # bumped every time the keep time cleared the text (the GUI watches it)
        self._delivered_at: float | None = None
        self._visible_sent: bool | None = None

        self.target_text = ""
        self.chatbox_text = ""
        self.osc_glyphs: list[int] = []       # what we believe the avatar currently shows
        self._resync_pointer = 0
        self.detect_step = 0                  # 0 idle, 1-4 detecting, -1 manual reset pending
        self._detect_answered = False
        self.osc_server = None

        self.osc_client = udp_client.SimpleUDPClient(config.osc_ip, config.osc_port)
        self.osc_timer = RepeatedTimer(config.osc_delay, self.osc_timer_loop)
        self.osc_chatbox_timer = RepeatedTimer(config.osc_chatbox_delay, self.osc_chatbox_loop)
        self._reset_shadow()
        if autostart:
            self.start()

    # ---------------------------------------------------------------- properties

    @classmethod
    def _normalise_sync_params(cls, n: int) -> int:
        value = int(n)
        if value < cls.SYNC_PARAMS_MIN:
            return cls.SYNC_PARAMS_MIN
        if value > cls.SYNC_PARAMS_MAX:
            return cls.SYNC_PARAMS_MAX
        if value % 2:
            value += 1
        if TEXT_LENGTH % value:
            raise ValueError(
                f"Sync parameters must divide the {TEXT_LENGTH} text slots: "
                f"one of {valid_sync_sizes()} (a stock avatar only supports these)")
        return value

    @property
    def pointer_count(self) -> int:
        return math.ceil(self.text_length / self.sync_params) if self.sync_params else 0

    @property
    def slot_total(self) -> int:
        return self.pointer_count * self.sync_params

    @property
    def wire_format(self) -> int:
        """The page format actually written to the avatar. Glyph indices mean the same thing in every format, so a
        charset can be sent in whichever format the avatar was built for as long as its pages are addressable."""
        for fmt in (self.detected_page_format, self.declared_page_format):
            if fmt is not None:
                return fmt
        return self.charset.page_format

    @property
    def page_format(self) -> int:
        return self.wire_format

    @property
    def visible(self) -> bool:
        return self.keep_seconds != 0

    def _reset_shadow(self) -> None:
        """Forget what the avatar shows (it is blank now): also forget which groups ever held text."""
        self.osc_glyphs = [BLANK] * max(self.slot_total, self.text_length)
        self._touched: set[int] = set()       # groups written with text since the last clear (the heal pass resends these)
        self._heal_queue: list[int] = []
        self._heal_due: float | None = None

    # ---------------------------------------------------------------- public API

    def set_text(self, text: str) -> None:
        """Set the desired text. Text that lays out to nothing (empty / whitespace) clears the avatar on the next tick."""
        with self._state_lock:
            self.target_text = text

    def clear(self) -> None:
        """Clear the avatar text right now (the Clear button)."""
        with self._state_lock:
            self.target_text = ""
            self._clear_avatar()

    def resend(self) -> None:
        """Forget what the avatar shows without clearing it: every group is rewritten on the next ticks."""
        with self._state_lock:
            self.osc_glyphs = [UNKNOWN] * max(self.slot_total, self.text_length)
            self._heal_queue, self._heal_due = [], None
            self._delivered_at = None

    def set_charset(self, charset: Charset, path: str | None = None) -> None:
        """Switch glyph set at runtime. Glyph indices change meaning, so everything is rewritten."""
        with self._state_lock:
            self.charset = charset
            self.charset_path = path
            self._touched = set()
            self.resend()

    def set_declared_page_format(self, fmt: int | None) -> None:
        with self._state_lock:
            self.declared_page_format = None if fmt is None else check_format(fmt)
            self.resend()

    def set_keep_time(self, seconds) -> None:
        """None = never clear, 0 = KAT hidden, > 0 = clear that many seconds after the text was delivered."""
        value = parse_keep_time(seconds)
        with self._state_lock:
            self.keep_seconds = value
            if self._delivered_at is not None:
                self._delivered_at = self._clock()        # a new setting restarts the countdown
            self._sync_visibility()

    def seconds_until_clear(self) -> float | None:
        with self._state_lock:
            if not self.keep_seconds or self._delivered_at is None:
                return None
            return max(0.0, self.keep_seconds - (self._clock() - self._delivered_at))

    def speed_to_delay(self, speed: float) -> float:
        speed = min(max(float(speed), 0.0), 1.0)
        return self.osc_delay_slow * (self.osc_delay_fast / self.osc_delay_slow) ** speed

    def set_speed(self, speed: float) -> None:
        """0 = slowest ... 1 = fastest rolling. Takes effect on the next timer tick."""
        with self._state_lock:
            self.speed = min(max(float(speed), 0.0), 1.0)
            self.osc_timer.interval = self.speed_to_delay(self.speed)

    def set_sync_params(self, sync_params: int) -> None:
        """0 = auto-detect (needs the OSC server). Otherwise a manual count that must be even and
        divide the text slots (2, 4, 8 or 16); anything else raises ValueError."""
        with self._state_lock:
            value = int(sync_params)
            if value == 0:
                self.osc_enable_server = True
                self.osc_start_server()
                return
            self.sync_params = self._normalise_sync_params(value)
            self.sync_params_last = self.sync_params
            self.last_error = None
            self.detect_note = None
            self.detect_step = -1
            self.detected_page_format = None
            self.format_error = None
            self.osc_stop_server()
            self.osc_enable_server = False
            self._reset_shadow()

    def start(self) -> None:
        with self._state_lock:
            self._visible_sent = None
            self._sync_visibility()
            self._send(self.param_pointer, self.POINTER_CLEAR)
            self._send_all_sync(0.0, self.sync_params)
            self._reset_shadow()
            if self.osc_enable_server:
                self.osc_start_server()
            self.osc_chatbox_timer.start()
            self.osc_timer.start()

    def stop(self) -> None:
        self.osc_chatbox_timer.stop()
        self.osc_timer.stop()
        with self._state_lock:
            try:
                self._send(self.param_visible, False)
            except OSError:
                pass
            self._visible_sent = False
            self.osc_stop_server()

    def show(self) -> None:
        self._send_visible(True)

    def hide(self) -> None:
        self._send_visible(False)

    def _send_visible(self, value: bool) -> None:
        self._send(self.param_visible, value)
        self._visible_sent = value

    def _sync_visibility(self) -> None:
        """Send KAT_Visible only when it differs from what the avatar was last told."""
        if self._visible_sent != self.visible:
            self._send_visible(self.visible)

    # ---------------------------------------------------------------- OSC server / detection

    def osc_start_server(self) -> None:
        with self._state_lock:
            if self.osc_server is not None:
                self.detect_step = 1        # already listening: just re-detect
                self.last_error = None
                return
            try:
                d = dispatcher.Dispatcher()
                d.map(self.PREFIX + self.param_char + "*", self._on_char_param)
                d.map(self.PREFIX + self.param_page + "*", self._on_page_param)
                d.map(self.PREFIX + self.param_speed, self._on_speed_param)
                d.map(self.AVATAR_CHANGE + "*", self._on_avatar_change)
                server = osc_server.ThreadingOSCUDPServer((self.osc_server_ip, self.osc_server_port), d)
                threading.Thread(target=server.serve_forever, daemon=True).start()
                self.osc_server = server
                self.detect_step = 1
                self.last_error = None
            except Exception as exc:        # OSError (port in use) or anything the OSC library raises
                self.osc_server = None
                self.osc_enable_server = False
                self.detect_step = 0
                self.last_error = (f"OSC server could not bind {self.osc_server_ip}:{self.osc_server_port}: {exc}"
                                   if isinstance(exc, OSError) else f"OSC server could not start: {exc}")
                print(f"KAT OSC: {self.last_error}")

    def osc_stop_server(self) -> None:
        with self._state_lock:
            server, self.osc_server = self.osc_server, None
        if server is not None:              # shutdown() outside the lock: handler threads need it to finish
            server.shutdown()
            server.server_close()

    def _on_char_param(self, address, *args) -> None:
        with self._state_lock:
            if self.detect_step > 0:
                self._detect_answered = True
                suffix = address[len(self.PREFIX + self.param_char):]
                if suffix.isdigit():
                    self.sync_params = min(max(self.sync_params, int(suffix) + 1), self.SYNC_PARAMS_MAX)

    def _on_page_param(self, address, *args) -> None:
        with self._state_lock:
            if self.detect_step > 0:
                suffix = address[len(self.PREFIX + self.param_page):]
                if suffix.isdigit():
                    self._page_params_count = max(self._page_params_count, int(suffix) + 1)

    def _on_speed_param(self, address, *args) -> None:
        if args and isinstance(args[0], (int, float)) and not isinstance(args[0], bool):
            self.set_speed(args[0])

    def _on_avatar_change(self, address, *args) -> None:
        with self._state_lock:
            self.detect_step = 1
            self.detected_page_format = None
            self.format_error = None
            self._visible_sent = None       # the new avatar has its own KAT_Visible: tell it again

    def _detect_step(self) -> bool:
        """One tick of sync-param detection. True = this tick is consumed."""
        step = self.detect_step
        if self.osc_server is None or step <= 0:
            return False
        self._send(self.param_pointer, self.POINTER_CLEAR)
        if step == 1:
            self.sync_params = 0
            self._page_params_count = 0
            self._detect_answered = False
            self.detect_note = None
            self.format_error = None                # a fresh detection re-judges the avatar page layout
            self.detected_page_format = None
            self._send_all_sync(0.0, detect=True)
            self.detect_step = 2
        elif step == 2:
            self._send_all_sync(symbol_to_float(self.TEST_SYMBOL), detect=True)
            self.detect_step = 3
        elif step == 3:
            self._send_all_sync(0.0, detect=True)
            self.detect_step = 4
        else:
            if not self._detect_answered:
                self.detect_note = (f"no reply from the avatar -- assuming {self.sync_params_last} sync parameters. "
                                    "Is OSC enabled in VRChat and KAT installed on this avatar?")
            self.sync_params = self._normalise_sync_params(self.sync_params) if self.sync_params else self.sync_params_last
            self.sync_params_last = self.sync_params
            self.detect_step = 0
            self._reset_shadow()
            self._verify_page_format()
            return False
        return True

    # ---------------------------------------------------------------- page format

    def _verify_page_format(self) -> None:
        """Read the avatar's page format off its KAT_PageSync parameter layout."""
        seen = self._page_params_count
        if not seen:
            return                                  # no PageSync response: not enough evidence, keep declared/charset format
        inferred = infer_format_from_param_counts(self.sync_params, seen)
        if inferred is None:
            self.format_error = (f"the avatar exposes {seen} KAT_PageSync parameter(s) for {self.sync_params} CharSync; "
                                 f"that matches neither page format ({', '.join(str(f) for f in sorted(PAGE_FORMATS))}). "
                                 "Re-run detection; if it persists the avatar's animator/parameters are inconsistent.")
            self.last_error = self.format_error
            print(f"KAT OSC: {self.format_error}")
            return
        self.detected_page_format = inferred

    def format_status(self) -> str:
        if self.format_error:
            return "PAGE FORMAT ERROR"
        fmt = self.wire_format
        if self.detected_page_format is not None:
            text = f"page format {fmt} (detected from avatar)"
        elif self.declared_page_format is not None:
            text = f"page format {fmt} (set manually, not verified)"
        else:
            text = f"page format {fmt} (from charset, not verified)"
        if fmt != self.charset.page_format:
            text += f"; charset is format {self.charset.page_format}"
        return text

    def _send_all_sync(self, value: float, count: int | None = None, detect: bool = False) -> None:
        n = self.SYNC_PARAMS_MAX if count is None else count
        for i in range(n):
            self._send(f"{self.param_char}{i}", value)
        # Detection probes the maximum page-parameter surface used by the supported formats. The avatar only echoes
        # parameters that exist in its installed controller, so the observed count identifies the page layout.
        pages = max(page_param_count(n, f) for f in PAGE_FORMATS) if detect else page_param_count(n, self.wire_format)
        for j in range(pages):
            self._send(f"{self.param_page}{j}", value)

    # ---------------------------------------------------------------- loops

    def osc_chatbox_loop(self) -> None:
        with self._state_lock:
            if not self.osc_enable_chatbox:
                return
            text = self.target_text.replace("\n", " ")[:CHATBOX_LIMIT]
            if text == self.chatbox_text:
                return
            try:
                self.osc_client.send_message(self.CHATBOX, [text, True])
            except OSError:
                return
            self.chatbox_text = text

    def osc_timer_loop(self) -> None:
        # One coherent state snapshot under one re-entrant lock, so GUI/API calls and OSC callbacks cannot interleave
        # midway through a dirty-group calculation or a detection transition.
        with self._state_lock:
            try:
                self._tick()
                self.send_error = None
            except OSError as exc:
                # e.g. connection reset while VRChat is closed. Nothing was recorded as delivered, so the same work is
                # simply retried on the next tick.
                self.send_error = f"OSC send failed ({exc}); is VRChat running with OSC enabled?"

    def _tick(self) -> None:
        if self._detect_step() or self.sync_params == 0:
            return
        if self.detect_step == -1:
            self._send_all_sync(0.0)
            self.detect_step = 0
            return
        self._sync_visibility()
        now = self._clock()
        target = self._layout(self.target_text)

        # Text that lays out to nothing (everything deleted, or only spaces/newlines): the avatar must be cleared too.
        if all(g == BLANK for g in target):
            if any(g != BLANK for g in self.osc_glyphs):
                self._clear_avatar()
            self._delivered_at = None
            return

        if self.keep_seconds and self._delivered_at is not None and now - self._delivered_at >= self.keep_seconds:
            self.target_text = ""
            self._clear_avatar()
            self.expired_count += 1
            return

        if self.format_error:
            # Loud, once: never encode for a layout the avatar cannot decode.
            if not self._format_notice_printed:
                self._format_notice_printed = True
                print(f"KAT OSC: refusing to send: {self.format_error}")
            return
        dirty = self._dirty_groups(target)
        if dirty:
            self._heal_queue, self._heal_due = [], None        # edits first, healing restarts once we are idle again
            self._delivered_at = None
            # Never auto-clear for a partial edit: each pointer write updates only its own group; the Unity animator
            # holds every other group's value.
            self._update_pointer(dirty[0], target)
            return
        if self._delivered_at is None:
            self._delivered_at = now                            # fully delivered: the keep-time countdown starts here
        self._idle_tick(target)

    def _clear_avatar(self) -> None:
        self._send(self.param_pointer, self.POINTER_CLEAR)
        self._send_all_sync(0.0, self.sync_params)
        self._reset_shadow()
        self._delivered_at = None

    def _group(self, glyphs: list[int], pointer_index: int) -> list[int]:
        lo = pointer_index * self.sync_params
        return glyphs[lo:lo + self.sync_params]

    def _dirty_groups(self, target: list[int]) -> list[int]:
        return [p for p in range(self.pointer_count) if self._group(target, p) != self._group(self.osc_glyphs, p)]

    def _idle_tick(self, target: list[int]) -> None:
        """Nothing changed; either perform the configured periodic heal pass or the continuous refresh mode."""
        if self.resync_interval <= 0:
            self._resync_pointer = (self._resync_pointer + 1) % self.pointer_count
            self._update_pointer(self._resync_pointer, target)
            return
        now = self._clock()
        if not self._heal_queue:
            if self._heal_due is None:
                self._heal_due = now + min(self.resync_first_delay, self.resync_interval)
                return
            if now < self._heal_due:
                return
            self._heal_queue = sorted(self._touched)
            if not self._heal_queue:
                self._heal_due = now + self.resync_interval
                return
        self._update_pointer(self._heal_queue.pop(0), target)
        if not self._heal_queue:
            self._heal_due = self._clock() + self.resync_interval

    def _update_pointer(self, pointer_index: int, target: list[int]) -> None:
        if self.format_error:
            raise KatFormatMismatch(self.format_error)
        fmt = self.wire_format
        if (fmt != 2 and self.detected_page_format is None and not self._format_notice_printed):
            # No readback to verify against (manual sync size). Say so once instead of failing silently later.
            self._format_notice_printed = True
            print(f"KAT OSC: sending page format {fmt}; the avatar's format is not verified (manual sync "
                  "size). If glyphs look wrong, the avatar was probably built for a different format "
                  "(--page-format 2|3).")
        m = self.sync_params
        lo = pointer_index * m
        group = target[lo:lo + m]
        chars, pages = encode_group(group, fmt)
        self._send(self.param_pointer, pointer_index + 1)
        for i, v in enumerate(chars):
            self._send(f"{self.param_char}{i}", v)
        for j, v in enumerate(pages):
            self._send(f"{self.param_page}{j}", v)
        if any(g != BLANK for g in group):
            self._touched.add(pointer_index)
        self.osc_glyphs[lo:lo + m] = group

    # ---------------------------------------------------------------- layout

    def _glyph_for(self, ch: str) -> int:
        """Glyph index for a character; a glyph the avatar's page format cannot address falls back like an unknown one."""
        g = self.charset.index_of(ch)
        if g > max_glyph_index(self.wire_format):
            return self.charset.fallback_index
        return g

    def undrawable_chars(self, text: str) -> list[str]:
        """Distinct characters of ``text`` that will appear as the fallback glyph (not in the charset, or on a page the
        avatar's format cannot address)."""
        out: dict[str, None] = {}
        for ch in self.charset.expand_emoji(text):
            if ch.isspace() and ch != "\u3000":
                continue
            if ch == "\u3000":
                continue
            if not self.charset.contains(ch) or self.charset.index_of(ch) > max_glyph_index(self.wire_format):
                out[ch] = None
        return list(out)

    def measure_text_slots(self, text: str, padded: bool = True) -> int:
        """Return the exact number of layout slots the client would consume before the 128-slot cap.

        This intentionally uses the same row and sync-group guards as ``_layout`` so the GUI does not
        underestimate space for wide glyphs that need a one-slot spacer at a row/group boundary.
        ``padded=True`` (the capacity check) counts the last line as a whole row; ``padded=False`` counts only the slots
        actually used by it (what a "slots used" indicator should show).
        """
        return len(self._layout_rows(text, pad_last=padded))

    def _layout_rows(self, text: str, pad_last: bool = True) -> list[int]:
        """Build rows with their required padding, but without final-capacity padding."""
        width = self.line_length
        group = self.sync_params or 1
        slots: list[int] = []
        text = self.charset.expand_emoji(text)   # :sob: and friends become one glyph each (does nothing if the charset has no emoji)
        lines = text.split("\n")
        for n, line in enumerate(lines):
            start = len(slots)
            for ch in line:
                if ch == "\u3000":                       # ideographic space: two blank slots
                    g, wide = BLANK, True
                elif ch.isspace():
                    # Tab, CR and other non-newline whitespace have no glyph: show a normal
                    # space instead of the "?" fallback. (lines come from text.split("\n"),
                    # so ch is never "\n" here.)
                    g, wide = BLANK, False
                else:
                    g = self._glyph_for(ch)
                    wide = self.charset.is_wide(g)
                guard = 0
                while wide and guard < width + group and (
                    len(slots) % width == width - 1
                    or (group > 1 and len(slots) % group == group - 1)
                ):
                    slots.append(BLANK)
                    guard += 1
                slots.append(g)
                if wide:
                    slots.append(BLANK if g == BLANK else FOLLOW)
            if n == len(lines) - 1 and not pad_last:
                break
            used = len(slots) - start
            rows = max(math.ceil(used / width), 1)
            slots.extend([BLANK] * (rows * width - used))
        return slots

    def _layout(self, text: str) -> list[int]:
        """Text -> one glyph index per slot. Wide glyphs become [glyph, FOLLOW] and never straddle a
        row, NOR a sync_params-sized OSC transmission group. A group boundary matters just as much as
        a row boundary: dirty groups are sent (and can be *rendered*) independently and one tick apart,
        so a wide glyph split across two groups shows up as a broken/half-drawn character whenever its
        lead half lands as the last slot of one group and its FOLLOW half as the first slot of the next
        -- e.g. after any run of narrow (1-slot) glyphs whose count leaves an odd remainder mod group."""
        slots = self._layout_rows(text)
        slots = slots[:self.text_length]
        if slots and self.charset.is_wide(slots[-1]) and len(slots) == self.text_length:
            slots[-1] = BLANK                            # lead whose FOLLOW got cut off
        slots.extend([BLANK] * (self.text_length - len(slots)))
        slots.extend([BLANK] * (self.slot_total - len(slots)))
        return slots

    # ---------------------------------------------------------------- helpers

    def _send(self, name: str, value) -> None:
        self.osc_client.send_message(self.PREFIX + name, value)


class RepeatedTimer:
    """Calls ``function`` every ``interval`` seconds on a daemon thread. stop() cannot be undone by a tick that is
    already running, and an exception in ``function`` is reported without ending the schedule."""

    def __init__(self, interval: float, function, *args, **kwargs):
        self.interval, self.function, self.args, self.kwargs = interval, function, args, kwargs
        self._timer: Timer | None = None
        self._lock = threading.Lock()
        self.is_running = False

    def _schedule(self) -> None:
        self._timer = Timer(self.interval, self._run)
        self._timer.daemon = True
        self._timer.start()

    def _run(self) -> None:
        with self._lock:
            if not self.is_running:
                return
            self._schedule()
        try:
            self.function(*self.args, **self.kwargs)
        except Exception:                                   # keep ticking; the next tick retries
            traceback.print_exc()

    def start(self) -> None:
        with self._lock:
            if not self.is_running:
                self.is_running = True
                self._schedule()

    def stop(self) -> None:
        with self._lock:
            self.is_running = False
            if self._timer:
                self._timer.cancel()
