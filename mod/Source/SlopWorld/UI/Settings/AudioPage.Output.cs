using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    // Daemon PCM routing is independent of RimWorld's mixer and ncspot's output.
    public sealed partial class AudioPage
    {
        Wire.AudioOutputs _outputs;
        string _outputsEndpoint;
        string _outputsError;
        int _outputsRequest;
        bool _outputsDisposed;
        bool _outputsLoading;

        void RefreshOutputs()
        {
            string endpoint = DaemonClient.BaseUrl;
            if (_outputsEndpoint != endpoint)
                _outputs = null;
            _outputsEndpoint = endpoint;
            _outputsError = null;
            _outputsLoading = true;
            int request = ++_outputsRequest;
            bool Current() =>
                !_outputsDisposed && request == _outputsRequest && endpoint == DaemonClient.BaseUrl;
            DaemonClient.Get<Wire.AudioOutputs>(
                WireProtocol.Routes.AudioOutputs,
                result =>
                {
                    if (!Current())
                        return;
                    _outputs = result;
                    _outputsLoading = false;
                },
                error =>
                {
                    if (!Current())
                        return;
                    _outputsError = error;
                    _outputsLoading = false;
                }
            );
        }

        void DrawOutput(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Radio output");
            if (!SessionHub.Instance.Capabilities.AudioPlayback)
            {
                UiLayout.Note(l, "Radio playback is unavailable on this host.");
                return;
            }
            if (!_loaded || _cfg == null)
            {
                UiLayout.Note(l, _error ?? "Loading audio settings…");
                return;
            }
            // Capture this endpoint's draft, never mutate whichever endpoint is current when
            // an already open menu is clicked.
            var config = _cfg;
            string endpoint = DaemonClient.BaseUrl;
            var outputs = _outputsEndpoint == endpoint ? _outputs : null;
            var devices = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    AutoOutputLabel(outputs, config.AudioBackend),
                    () => config.AudioDevice = ""
                ),
            };
            if (outputs != null)
                foreach (var device in outputs.Devices)
                {
                    string id = device.Id;
                    devices.Add(
                        new FloatMenuOption(DeviceLabel(device), () => config.AudioDevice = id)
                    );
                }
            var selected = outputs?.Devices.FirstOrDefault(device =>
                device.Id == config.AudioDevice
            );
            string label =
                string.IsNullOrEmpty(config.AudioDevice)
                    ? AutoOutputLabel(outputs, config.AudioBackend)
                : selected != null ? DeviceLabel(selected)
                : config.AudioDevice + " (unavailable)";
            UiControls.Select(l, "Device", label, devices, out _);

            var backends = new List<FloatMenuOption>
            {
                new FloatMenuOption(AutoBackendLabel(outputs), () => config.AudioBackend = "auto"),
                new FloatMenuOption("PulseAudio", () => config.AudioBackend = "pulse"),
                new FloatMenuOption("System default", () => config.AudioBackend = "default"),
            };
            string backend =
                config.AudioBackend == "pulse" ? "PulseAudio"
                : config.AudioBackend == "default" ? "System default"
                : AutoBackendLabel(outputs);
            UiControls.Select(l, "Audio system", backend, backends, out _);
            if (!string.IsNullOrEmpty(outputs?.ActiveDevice))
            {
                var active = outputs.Devices.FirstOrDefault(device =>
                    device.Id == outputs.ActiveDevice
                );
                // An unopened radio stream has no active device, even when routing is valid.
                UiLayout.Note(
                    l,
                    "Using: " + (active == null ? outputs.ActiveDevice : DeviceLabel(active))
                );
            }
            if (_outputsEndpoint == endpoint && !string.IsNullOrEmpty(_outputsError))
                UiLayout.Validation(l, _outputsError);
            if (
                UiLayout.Button(
                    l,
                    _outputsLoading && _outputsEndpoint == endpoint
                        ? "Refreshing…"
                        : "Refresh devices"
                )
            )
                RefreshOutputs();
        }

        static string AutoOutputLabel(Wire.AudioOutputs outputs, string backend)
        {
            if (
                outputs == null
                || !outputs.AutoDevices.TryGetValue(backend, out string id)
                || string.IsNullOrEmpty(id)
            )
                return OptionLabels.Auto();
            var device = outputs.Devices.FirstOrDefault(candidate => candidate.Id == id);
            return OptionLabels.Auto(device == null ? id : DeviceLabel(device));
        }

        static string AutoBackendLabel(Wire.AudioOutputs outputs)
        {
            if (outputs == null || !outputs.AutoDevices.TryGetValue("auto", out string id))
                return OptionLabels.Auto();
            return OptionLabels.Auto(
                id == "pulse" ? "PulseAudio"
                : id == "default" ? "System default"
                : null
            );
        }

        static string DeviceLabel(Wire.AudioOutputDevice device) =>
            device.Id == "pulse" ? "PulseAudio"
            : device.Id == "default" ? "System default"
            : device.Name == device.Id ? device.Id
            : device.Name + " (" + device.Id + ")";
    }
}
