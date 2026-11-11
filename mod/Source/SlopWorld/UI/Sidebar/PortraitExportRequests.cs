using System;
using System.IO;
using System.Text.Json;

namespace SlopWorld
{
    // Profile-local mailbox only. The caller owns main-thread scheduling and GPU
    // readback; requests contain an identity, never code or a destination path.
    internal sealed class PortraitExportRequests
    {
        readonly string root;
        string handled;

        internal PortraitExportRequests(string root, int processId)
        {
            this.root = root;
            Directory.CreateDirectory(root);
            Publish("available", JsonSerializer.Serialize(new { pid = processId }));
        }

        internal void Poll(Action capture)
        {
            string request = Path.Combine(root, "request");
            if (!File.Exists(request))
                return;
            string id;
            using (var reader = File.OpenText(request))
            {
                var buffer = new char[33];
                int length = reader.ReadBlock(buffer, 0, buffer.Length);
                if (length != 32)
                    return;
                id = new string(buffer, 0, length);
            }
            foreach (char c in id)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))
                    return;
            if (id == handled)
                return;
            handled = id;
            string error = null;
            try
            {
                capture();
            }
            catch (Exception e)
            {
                error = e.Message.Length <= 512 ? e.Message : e.Message.Substring(0, 512);
            }
            // Capture publishes its manifest first. A response acknowledges only
            // this request, including failure; it never retries every GUI frame.
            Publish(
                "response",
                JsonSerializer.Serialize(
                    new
                    {
                        version = 1,
                        id,
                        ok = error == null,
                        error,
                    }
                )
            );
        }

        void Publish(string name, string value)
        {
            string path = Path.Combine(root, name);
            File.WriteAllText(path + ".partial", value);
            File.Move(path + ".partial", path, true);
        }
    }
}
