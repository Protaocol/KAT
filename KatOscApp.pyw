# Avatar Text OSC App for VRChat - paged glyph protocol (KAT v2)
# Derived from KatOscApp, Copyright (C) 2022 KillFrenzy / Evan Tran
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
import sys

from pathlib import Path
from tkinter import Button, Entry, Frame, Label, OptionMenu, StringVar, Text, Tk, filedialog, messagebox

import katosc
from kat_app import AppController, apply_saved_defaults, load_settings, pick_charset, settings_path
from katosc import KatOsc, KatOscConfig

try:
    import opencc
    CONVERTER = opencc.OpenCC("t2s")        # traditional -> simplified, only for characters the charset cannot draw
except Exception:                           # optional: without it text is sent exactly as typed
    CONVERTER = None
    print("KAT: opencc is not installed; traditional characters missing from the charset will show as '?'", file=sys.stderr)

BG, PANEL, BUTTON, BUTTON_HOVER, TEXT_DIM, TEXT_BG = "#333", "#393939", "#444", "#666", "#aaa", "#222"
ERROR_FG, OK_FG = "#f88", "#aaa"
SYNC_CHOICES = ("Auto", "2", "4", "8", "16")
FORMAT_CHOICES = ("Auto", "2", "3")
POLL_MS = 200
KEEP_APPLY_DELAY_MS = 500


class KatOscApp:
    def __init__(self, args):
        self.window = window = Tk()
        window.withdraw()
        window.title("KAT OSC")
        window.configure(bg=BG)
        window.resizable(False, False)
        window.protocol("WM_DELETE_WINDOW", self._close)

        # ---- which charset? --cli path, else the last one used, else ask. Never the current directory.
        saved = load_settings()
        args = apply_saved_defaults(args, saved)
        picked = pick_charset(args.charset, getattr(args, "saved_charset", None), self._ask_charset,
                              lambda message: messagebox.showerror("KAT charset", message))
        if picked is None:
            window.destroy()
            raise SystemExit(0)
        charset, charset_path = picked

        config = KatOscConfig.from_args(args)
        config.charset_path = charset_path
        self.kat = KatOsc(config, charset=charset, autostart=False)
        settings = dict(saved)
        settings["charset"] = charset_path
        self.ctl = AppController(self.kat, settings, CONVERTER, settings_path())
        self._busy = False
        self._keep_job = None

        self._build(args)
        self.kat.start()
        window.deiconify()
        self._remember()
        self._poll()

    # ------------------------------------------------------------------ construction

    def _ask_charset(self):
        return filedialog.askopenfilename(
            title="Select your KAT charset.json",
            filetypes=[("KAT charset", "*.json"), ("All files", "*.*")])

    def _button(self, parent, text, command, width=10):
        return Button(parent, text=text, command=command, border=0, fg="#ddd", bg=BUTTON, activeforeground="#ddd",
                      activebackground=BUTTON_HOVER, width=width, height=2)

    def _menu(self, parent, variable, choices):
        menu = OptionMenu(parent, variable, *choices)
        menu.config(indicatoron=0, border=0, highlightthickness=0, fg="#ddd", bg=BUTTON, activeforeground="#ddd",
                    activebackground=BUTTON_HOVER, width=6, height=2)
        return menu

    def _label(self, parent, text, **kw):
        return Label(parent, text=text, border=0, fg="#ddd", bg=kw.pop("bg", PANEL), **kw)

    def _build(self, args):
        window, kat = self.window, self.kat

        # ---- text box
        self.gui_text = Text(window, font=("Courier New", 24), width=kat.line_length, height=kat.line_count, border=0,
                             wrap="char", fg="#fff", bg=TEXT_BG, insertbackground="#fff", undo=True)
        self.gui_text.grid(column=0, row=0, padx=10, pady=(10, 2))
        # <<Modified>> fires after ANY change of the content: typing, IME commit, paste, cut, undo, drag and drop.
        # (<KeyRelease> misses most of those, and <Key> fires before the character is inserted.)
        self.gui_text.bind("<<Modified>>", self._on_modified)
        self.gui_text.focus_set()

        self.slots_var = StringVar(window, value=f"0/{kat.text_length}")
        self.slots_label = Label(window, textvariable=self.slots_var, border=0, fg=TEXT_DIM, bg=BG, anchor="e")
        self.slots_label.grid(column=0, row=1, padx=12, sticky="e")

        # ---- controls
        bar = Frame(window, bg=BG)
        bar.grid(column=0, row=2, padx=10, pady=(4, 0), sticky="we")

        self._label(bar, "Keep text time (s)", width=17, height=2).grid(column=0, row=0, sticky="we")
        self.keep_var = StringVar(window, value=args.keep_time or "")
        self.keep_entry = Entry(bar, textvariable=self.keep_var, width=7, justify="center", fg="#fff", bg=BUTTON,
                                insertbackground="#fff", border=0, highlightthickness=1, highlightbackground=BG,
                                highlightcolor=BUTTON_HOVER)
        self.keep_entry.grid(column=1, row=0, padx=(2, 12), ipady=7)
        self.keep_entry.bind("<Return>", lambda event: self._apply_keep())
        self.keep_entry.bind("<FocusOut>", lambda event: self._apply_keep())
        self.keep_var.trace_add("write", lambda *_: self._schedule_keep())

        self._label(bar, "Sync", width=5, height=2).grid(column=2, row=0)
        self.sync_var = StringVar(window, value="Auto" if not args.sync_params or args.sync_params == "0" else str(args.sync_params).title())
        self._menu(bar, self.sync_var, SYNC_CHOICES).grid(column=3, row=0, padx=(2, 8))
        self.sync_var.trace_add("write", lambda *_: self._on_sync())

        self._label(bar, "Format", width=7, height=2).grid(column=4, row=0)
        self.format_var = StringVar(window, value="Auto" if not args.page_format else str(args.page_format).title())
        self._menu(bar, self.format_var, FORMAT_CHOICES).grid(column=5, row=0, padx=(2, 12))
        self.format_var.trace_add("write", lambda *_: self._on_format())

        self._button(bar, "Send", self._send).grid(column=6, row=0, padx=(0, 4))
        self._button(bar, "Clear", self._clear).grid(column=7, row=0)

        hint = Label(window, text="Keep text time: empty = never clear   0 = hide KAT   number = clear after that many seconds",
                     border=0, fg=TEXT_DIM, bg=BG, anchor="w")
        hint.grid(column=0, row=3, padx=12, pady=(4, 0), sticky="w")

        # ---- charset row
        row = Frame(window, bg=BG)
        row.grid(column=0, row=4, padx=10, pady=(6, 0), sticky="we")
        self._label(row, "Charset", width=9, height=2).grid(column=0, row=0)
        self.charset_var = StringVar(window, value=Path(self.kat.charset_path).name)
        self.charset_label = Label(row, textvariable=self.charset_var, border=0, fg="#ddd", bg=PANEL, anchor="w", width=36, height=2)
        self.charset_label.grid(column=1, row=0, padx=(2, 4), sticky="we")
        self._button(row, "Browse...", self._choose_charset, width=12).grid(column=2, row=0)

        # ---- status
        self.status_label = Label(window, text="", border=0, fg=OK_FG, bg=BG, anchor="w", wraplength=600, justify="left")
        self.status_label.grid(column=0, row=5, padx=12, pady=(6, 10), sticky="w")
        self._apply_keep()          # the initial value (from --keep-time or the last session) goes through the same validation
        self._update_widgets()

    # ------------------------------------------------------------------ text box

    def _on_modified(self, event=None):
        if self._busy or not self.gui_text.edit_modified():
            return
        self.gui_text.edit_modified(False)
        text = self.gui_text.get("1.0", "end-1c")
        cursor = len(self.gui_text.get("1.0", "insert"))
        update = self.ctl.on_text_changed(text, cursor)
        if update.rewrite:
            self._write_box(update.text, update.cursor)
        self._update_widgets()

    def _write_box(self, text, cursor=None):
        self._busy = True
        try:
            self.gui_text.delete("1.0", "end")
            self.gui_text.insert("1.0", text)
            self.gui_text.mark_set("insert", f"1.0+{len(text) if cursor is None else cursor}c")
        finally:
            self.gui_text.edit_modified(False)
            self._busy = False

    def _send(self):
        update = self.ctl.send(self.gui_text.get("1.0", "end-1c"))
        if update.rewrite:
            self._write_box(update.text, update.cursor)
        self._update_widgets()
        self.gui_text.focus_set()

    def _clear(self):
        self.ctl.clear()
        self._write_box("")
        self._update_widgets()
        self.gui_text.focus_set()

    # ------------------------------------------------------------------ settings widgets

    def _schedule_keep(self):
        if self._keep_job is not None:
            self.window.after_cancel(self._keep_job)
        self._keep_job = self.window.after(KEEP_APPLY_DELAY_MS, self._apply_keep)

    def _apply_keep(self):
        if self._keep_job is not None:
            self.window.after_cancel(self._keep_job)
            self._keep_job = None
        error = self.ctl.apply_keep_time(self.keep_var.get())
        self.keep_entry.config(highlightbackground="#c44" if error else BG, highlightcolor="#c44" if error else BUTTON_HOVER)
        self._update_widgets()

    def _on_sync(self):
        self.ctl.set_sync_choice(self.sync_var.get())
        self._update_widgets()

    def _on_format(self):
        self.ctl.set_format_choice(self.format_var.get())
        self._update_widgets()

    def _choose_charset(self):
        current = Path(self.kat.charset_path).parent if self.kat.charset_path else None
        path = filedialog.askopenfilename(
            title="Select a KAT charset.json", initialdir=str(current) if current else None,
            filetypes=[("KAT charset", "*.json"), ("All files", "*.*")])
        if not path:
            return
        try:
            self.ctl.switch_charset(path)
        except ValueError as exc:
            messagebox.showerror("KAT charset", str(exc))          # the previous charset stays active
            return
        self.charset_var.set(Path(self.kat.charset_path).name)
        update = self.ctl.refresh()
        if update.rewrite:
            self._write_box(update.text, update.cursor)
        self._update_widgets()

    # ------------------------------------------------------------------ periodic

    def _update_widgets(self):
        text = self.gui_text.get("1.0", "end-1c")
        used, cap = self.kat.measure_text_slots(text, padded=False), self.kat.text_length
        self.slots_var.set(f"{used}/{cap}")
        self.slots_label.config(fg=ERROR_FG if used >= cap else TEXT_DIM)
        message, is_error = self.ctl.status()
        self.status_label.config(text=message, fg=ERROR_FG if is_error else OK_FG)

    def _poll(self):
        result = self.ctl.poll()
        if result.clear_box:
            self._write_box("")
        if result.alert:
            messagebox.showerror("KAT page format", result.alert)
        self._update_widgets()
        self._after_poll = self.window.after(POLL_MS, self._poll)

    # ------------------------------------------------------------------ lifecycle

    def run(self):
        self.window.mainloop()

    def _remember(self):
        """Persist what the window shows, so the next start comes up the same (the command line still wins)."""
        self.ctl.settings["sync_params"] = self.sync_var.get()
        self.ctl.settings["page_format"] = self.format_var.get()
        self.ctl.save()

    def _close(self):
        try:
            self._remember()
        finally:
            self.kat.stop()                 # hides KAT on the avatar and stops the OSC server
            self.window.destroy()


def main(argv=None):
    parser = argparse.ArgumentParser(description="KAT OSC client: type text, it appears on your avatar.")
    katosc.add_cli_arguments(parser)
    args = parser.parse_args(argv)
    try:
        app = KatOscApp(args)
    except SystemExit:
        raise
    except Exception as exc:                # bad charset / option values: show them where a .pyw user will see them
        root = Tk()
        root.withdraw()
        messagebox.showerror("KAT OSC configuration error", str(exc))
        root.destroy()
        raise SystemExit(2)
    app.run()


if __name__ == "__main__":
    main()
