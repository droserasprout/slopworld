namespace SlopWorld
{
    // The OpenURI portal restricts choices to MIME handlers. GTK's native application
    // chooser can also expose unrelated installed apps. Keep Python/GObject optional,
    // and fall back to the portal when the host cannot initialize GTK.
    internal static class NativeAppPicker
    {
        public static string Command(string path) =>
            "bash -lc " + PagerCommands.Quote(GtkCommand(path) + " || " + PortalCommand(path));

        static string GtkCommand(string path) =>
            "python3 -c " + PagerCommands.Quote(GtkChooserScript) + " " + PagerCommands.Quote(path);

        static string PortalCommand(string path)
        {
            const string bus = "gdbus call --session --dest org.freedesktop.portal.Desktop " +
                "--object-path /org/freedesktop/portal/desktop ";
            // OpenFile exists in v2, but ask=true only exists in v3. Refuse an older
            // portal rather than silently opening the default application instead of a chooser.
            // https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.OpenURI.html
            string version = bus + "--method org.freedesktop.DBus.Properties.Get " +
                "org.freedesktop.portal.OpenURI version";
            // gdbus converts handle argument 3 into a D-Bus Unix FD. The shell opens
            // the selected path on inherited descriptor 3 before gdbus marshals it.
            string open = bus + "--method org.freedesktop.portal.OpenURI.OpenFile " +
                PagerCommands.Quote("") + " 3 " + PagerCommands.Quote("{'ask': <true>}") +
                " 3<" + PagerCommands.Quote(path);
            return "(portal_version=$(" + version + ") && " +
                "[[ $portal_version =~ uint32[[:space:]]+([0-9]+) ]] && " +
                "(( BASH_REMATCH[1] >= 3 )) && " + open +
                ") || { echo 'Open With requires GTK or an OpenURI portal version 3 or newer.' >&2; exit 1; }";
        }

        const string GtkChooserScript = @"
import os
import sys
# Fork before GTK starts threads or connects to the display. Report readiness so
# a missing dependency/display can still trigger the portal fallback.
ready_read, ready_write = os.pipe()
if os.fork():
    os.close(ready_write)
    ready = os.read(ready_read, 1)
    os.close(ready_read)
    sys.exit(0 if ready == b'1' else 1)
os.close(ready_read)
os.setsid()
import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gio, Gtk

if not Gtk.init_check()[0]:
    sys.exit(1)
file = Gio.File.new_for_path(sys.argv[1])
dialog = Gtk.AppChooserDialog.new(None, Gtk.DialogFlags(0), file)
dialog.set_title('Open With')
dialog.get_widget().set_show_all(True)

# The daemon bounds file-action lifetime. Detach the interactive chooser and close
# inherited capture pipes so its lifetime follows the user's response instead.
with open(os.devnull, 'r+') as sink:
    for fd in (0, 1, 2):
        os.dup2(sink.fileno(), fd)
os.write(ready_write, b'1')
os.close(ready_write)
try:
    while dialog.run() == Gtk.ResponseType.OK:
        app = dialog.get_app_info()
        if app is None:
            continue
        try:
            app.launch([file], dialog.get_display().get_app_launch_context())
            break
        except Exception as error:
            message = Gtk.MessageDialog(
                transient_for=dialog, modal=True,
                message_type=Gtk.MessageType.ERROR,
                buttons=Gtk.ButtonsType.CLOSE,
                text='Could not open file')
            message.format_secondary_text(str(error))
            message.run()
            message.destroy()
finally:
    dialog.destroy()
";
    }
}
