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
            UiLayout.SectionHeading(l, "Daemon radio output");
            if (!SessionHub.Instance.Capabilities.AudioPlayback)
            {
                UiLayout.Note(l, "Daemon audio output is unavailable on this host.");
                return;
            }
            if (!_loaded || _cfg == null)
            {
                UiLayout.Note(l, _error ?? "Waiting for daemon settings");
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
            UiControls.Select(l, "Output", label, devices, out _);

            var backends = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    AutoOutputLabel(outputs, "auto"),
                    () => config.AudioBackend = "auto"
                ),
                new FloatMenuOption("PulseAudio-compatible", () => config.AudioBackend = "pulse"),
                new FloatMenuOption("PipeWire", () => config.AudioBackend = "pipewire"),
                new FloatMenuOption("System default", () => config.AudioBackend = "default"),
            };
            string backend =
                config.AudioBackend == "pulse" ? "PulseAudio-compatible"
                : config.AudioBackend == "pipewire" ? "PipeWire"
                : config.AudioBackend == "default" ? "System default"
                : AutoOutputLabel(outputs, "auto");
            UiControls.Select(l, "Backend", backend, backends, out _);
            if (outputs != null)
            {
                var active = outputs.Devices.FirstOrDefault(device =>
                    device.Id == outputs.ActiveDevice
                );
                UiLayout.Note(
                    l,
                    "Active: "
                        + (
                            string.IsNullOrEmpty(outputs.ActiveDevice) ? "none"
                            : active == null ? outputs.ActiveDevice
                            : DeviceLabel(active)
                        )
                );
            }
            if (_outputsEndpoint == endpoint && !string.IsNullOrEmpty(_outputsError))
                UiLayout.Validation(l, _outputsError);
            if (
                UiLayout.Button(
                    l,
                    _outputsLoading && _outputsEndpoint == endpoint
                        ? "Refresh outputs (loading)"
                        : "Refresh outputs"
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
            return OptionLabels.Auto(device == null ? id : device.Name);
        }

        static string DeviceLabel(Wire.AudioOutputDevice device) =>
            device.Name == device.Id ? device.Id : device.Name + " (" + device.Id + ")";
    }
}
