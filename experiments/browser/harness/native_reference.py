"""Capture through the desktop's permissioned screenshot portal, without input."""

import shutil
import sys
from pathlib import Path
from urllib.parse import unquote
from urllib.parse import urlparse

from gi.repository import Gio
from gi.repository import GLib

connection = Gio.bus_get_sync(Gio.BusType.SESSION, None)
loop = GLib.MainLoop()
result = {'error': 'Screenshot portal did not respond'}
destination = Path(sys.argv[1]).resolve()


def response(_connection, _sender, _path, _interface, _signal, parameters, _data):
    code, values = parameters.unpack()
    if code:
        result['error'] = f'Screenshot portal declined or failed (response {code})'
    else:
        uri = urlparse(values.get('uri', ''))
        if uri.scheme != 'file' or uri.netloc not in ('', 'localhost'):
            result['error'] = 'Portal returned a non-local screenshot'
        else:
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(unquote(uri.path), destination)
            result.clear()
            result['path'] = str(destination)
    loop.quit()


# Subscribe before requesting so even an immediate response cannot be lost.
token = 'slopweb_reference'
sender = connection.get_unique_name().lstrip(':').replace('.', '_')
request_path = f'/org/freedesktop/portal/desktop/request/{sender}/{token}'
subscription = connection.signal_subscribe(
    'org.freedesktop.portal.Desktop',
    'org.freedesktop.portal.Request',
    'Response',
    request_path,
    None,
    Gio.DBusSignalFlags.NONE,
    response,
    None,
)
connection.call_sync(
    'org.freedesktop.portal.Desktop',
    '/org/freedesktop/portal/desktop',
    'org.freedesktop.portal.Screenshot',
    'Screenshot',
    GLib.Variant('(sa{sv})', ('', {'handle_token': GLib.Variant('s', token), 'interactive': GLib.Variant('b', False)})),
    GLib.VariantType.new('(o)'),
    Gio.DBusCallFlags.NONE,
    30000,
    None,
)
print('Waiting for the desktop screenshot portal; approve its dialog if shown.', flush=True)
GLib.timeout_add_seconds(180, lambda: (loop.quit(), False)[1])
loop.run()
connection.signal_unsubscribe(subscription)
if 'error' in result:
    raise RuntimeError(result['error'])
print(result['path'])
