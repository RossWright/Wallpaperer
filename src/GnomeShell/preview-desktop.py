#!/usr/bin/env python3
"""Hide every window with GNOME's show-desktop shortcut, then put them back.

The next key or click brings the windows back. Closing this process does too.
"""

import signal
import sys
import time
from pathlib import Path

from gi.repository import Gio, GLib, GLibUnix

PORTAL_BUS = "org.freedesktop.portal.Desktop"
PORTAL_PATH = "/org/freedesktop/portal/desktop"
REMOTE = "org.freedesktop.portal.RemoteDesktop"
IDLE_BUS = "org.gnome.Mutter.IdleMonitor"
IDLE_PATH = "/org/gnome/Mutter/IdleMonitor/Core"
IDLE = "org.gnome.Mutter.IdleMonitor"

# X11 keysyms. The show-desktop binding is stored this way.
MODIFIERS = {
    "Super": 0xFFEB,    # Super_L
    "Primary": 0xFFE3,  # Control_L
    "Control": 0xFFE3,
    "Ctrl": 0xFFE3,
    "Alt": 0xFFE9,      # Alt_L
    "Shift": 0xFFE1,    # Shift_L
}
PRESSED = 1
RELEASED = 0

bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
sender = bus.get_unique_name()[1:].replace(".", "_")
loop = GLib.MainLoop()
session = None
shown = False
restored = False
token_path = None


def fail(message, code=1):
    print(message, file=sys.stderr)
    raise SystemExit(code)


def request_path(token):
    return f"/org/freedesktop/portal/desktop/request/{sender}/{token}"


def portal(interface, method, parameters, token):
    path = request_path(token)
    outcome = {}

    def on_response(_connection, _sender, _path, _interface, _signal, parameters):
        code, body = parameters.unpack()
        outcome["code"] = code
        outcome["body"] = body
        loop.quit()

    subscription = bus.signal_subscribe(
        PORTAL_BUS,
        "org.freedesktop.portal.Request",
        "Response",
        path,
        None,
        Gio.DBusSignalFlags.NONE,
        on_response,
    )
    try:
        bus.call_sync(
            PORTAL_BUS,
            PORTAL_PATH,
            interface,
            method,
            parameters,
            GLib.VariantType.new("(o)"),
            Gio.DBusCallFlags.NONE,
            -1,
            None,
        )
        loop.run()
    finally:
        bus.signal_unsubscribe(subscription)

    if "code" not in outcome:
        fail("The desktop portal did not answer.")
    if outcome["code"] == 1:
        fail("Permission was not granted.", 2)
    if outcome["code"] != 0:
        fail("The desktop portal rejected the preview.")
    return outcome["body"]


def variant_options(values):
    return {key: GLib.Variant(kind, value) for key, (kind, value) in values.items()}


def show_desktop_keys():
    try:
        settings = Gio.Settings.new("org.gnome.desktop.wm.keybindings")
        bindings = settings.get_strv("show-desktop")
    except Exception:
        bindings = ["<Super>d"]

    parsed = [keys for binding in bindings if (keys := parse_binding(binding))]
    if not parsed:
        parsed = [[0xFFEB, 0x64]]
    return min(parsed, key=len)


def parse_binding(binding):
    keys = []
    rest = binding
    while rest.startswith("<"):
        end = rest.find(">")
        if end < 1:
            return None
        name = rest[1:end]
        if name not in MODIFIERS:
            return None
        keys.append(MODIFIERS[name])
        rest = rest[end + 1 :]
    if len(rest) != 1 or not rest.isalpha() or not rest.islower():
        return None
    keys.append(ord(rest))
    return keys


def notify_key(keysym, state):
    # The session handle is an argument. The method lives on the portal object.
    bus.call_sync(
        PORTAL_BUS,
        PORTAL_PATH,
        REMOTE,
        "NotifyKeyboardKeysym",
        GLib.Variant("(oa{sv}iu)", (session, {}, keysym, state)),
        None,
        Gio.DBusCallFlags.NONE,
        -1,
        None,
    )


def chord():
    keys = show_desktop_keys()
    try:
        for key in keys:
            notify_key(key, PRESSED)
            time.sleep(0.05)
        time.sleep(0.08)
    finally:
        for key in reversed(keys):
            try:
                notify_key(key, RELEASED)
            except Exception:
                pass
            time.sleep(0.02)


def idletime():
    reply = bus.call_sync(
        IDLE_BUS,
        IDLE_PATH,
        IDLE,
        "GetIdletime",
        None,
        GLib.VariantType.new("(t)"),
        Gio.DBusCallFlags.NONE,
        -1,
        None,
    )
    return reply.unpack()[0]


def restore():
    global restored
    if restored:
        return
    restored = True
    if shown and session is not None:
        try:
            chord()
        except Exception as error:
            print(error, file=sys.stderr)
    if session is not None:
        try:
            bus.call_sync(
                PORTAL_BUS,
                session,
                "org.freedesktop.portal.Session",
                "Close",
                None,
                None,
                Gio.DBusCallFlags.NONE,
                -1,
                None,
            )
        except Exception:
            pass
    save_token()


def save_token():
    if not token_path or "restore_token" not in globals() or not restore_token:
        return
    try:
        Path(token_path).parent.mkdir(parents=True, exist_ok=True)
        Path(token_path).write_text(restore_token)
    except Exception:
        pass


def on_term():
    restore()
    loop.quit()
    return GLib.SOURCE_REMOVE


def main():
    global session, shown, token_path, restore_token
    token_path = sys.argv[1] if len(sys.argv) > 1 else None
    restore_token = ""
    if token_path and Path(token_path).is_file():
        restore_token = Path(token_path).read_text().strip()

    GLibUnix.signal_add(GLib.PRIORITY_DEFAULT, signal.SIGTERM, on_term)

    session = portal(
        REMOTE,
        "CreateSession",
        GLib.Variant(
            "(a{sv})",
            (variant_options({
                "handle_token": ("s", "wallpaperer_create"),
                "session_handle_token": ("s", "wallpaperer_session"),
            }),),
        ),
        "wallpaperer_create",
    ).get("session_handle")
    if not session:
        fail("The desktop portal did not open a session.")

    select = {
        "handle_token": ("s", "wallpaperer_select"),
        "types": ("u", 1),
        "persist_mode": ("u", 2),
    }
    if restore_token:
        select["restore_token"] = ("s", restore_token)
    portal(
        REMOTE,
        "SelectDevices",
        GLib.Variant("(oa{sv})", (session, variant_options(select))),
        "wallpaperer_select",
    )

    started = portal(
        REMOTE,
        "Start",
        GLib.Variant("(osa{sv})", (session, "", variant_options({
            "handle_token": ("s", "wallpaperer_start"),
        }))),
        "wallpaperer_start",
    )
    devices = int(started.get("devices", 1))
    if devices & 1 == 0:
        fail("Keyboard control was not granted.", 2)
    if isinstance(started.get("restore_token"), str):
        restore_token = started["restore_token"]
        save_token()

    chord()
    shown = True
    peak = 0

    def arm():
        nonlocal peak
        if restored:
            return GLib.SOURCE_REMOVE
        idle = idletime()
        peak = max(peak, idle)
        if peak >= 200 and idle < 40:
            restore()
            loop.quit()
            return GLib.SOURCE_REMOVE
        if idle < 200:
            return GLib.SOURCE_CONTINUE

        watch_id = None

        def on_active(_connection, _sender, _path, _interface, _signal, parameters):
            watch, = parameters.unpack()
            if watch_id is not None and watch == watch_id and not restored:
                restore()
                loop.quit()

        bus.signal_subscribe(
            IDLE_BUS,
            IDLE,
            "WatchFired",
            IDLE_PATH,
            None,
            Gio.DBusSignalFlags.NONE,
            on_active,
        )
        reply = bus.call_sync(
            IDLE_BUS,
            IDLE_PATH,
            IDLE,
            "AddUserActiveWatch",
            None,
            GLib.VariantType.new("(u)"),
            Gio.DBusCallFlags.NONE,
            -1,
            None,
        )
        watch_id = reply.unpack()[0]
        return GLib.SOURCE_REMOVE

    GLib.timeout_add(30, arm)
    loop.run()
    restore()


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as error:
        fail(str(error))
