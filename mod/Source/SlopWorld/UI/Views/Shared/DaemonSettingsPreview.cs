using System;
using UnityEngine;

namespace SlopWorld
{
    // Each editor owns its request state. A different draft or connection invalidates replies.
    // failed requests require an explicit retry rather than an HTTP request every frame.
    public sealed class DaemonSettingsPreview
    {
        readonly AsyncLoadState<SandboxPreviewData> _load = new AsyncLoadState<SandboxPreviewData>();
        string _key;
        Wire.SettingsPreviewRequest _body;

        public void Draw(Rect rect, Wire.SettingsPreviewRequest body, ref SmoothScroll scroll)
        {
            string key = DaemonClient.BaseUrl + "\n" + SessionHub.Instance.ConnectionGeneration + "\n" + SessionHub.Instance.Catalog.SettingsRevision;
            if (_key != key || !body.Equals(_body))
            {
                _key = key;
                _body = body.Clone();
                _load.Load((ok, fail) => DaemonClient.Post<Wire.SettingsPreview>(WireProtocol.Routes.SettingsPreview, body,
                    response => ok(SandboxPreviewData.FromWire(response)), fail));
            }
            var refresh = new Rect(rect.x, rect.yMax - UiTheme.BtnH, rect.width, UiTheme.BtnH);
            rect.height -= UiTheme.BtnH + UiTheme.GapS;
            if (UiButtons.Button(refresh, "Refresh preview", UiTheme.Btn.Ghost, !_load.Loading)) _key = null;
            if (_load.Loading) UiText.PlainStatusLabel(rect, "Resolving settings with the daemon", UiTheme.Dim);
            else if (!_load.HasValue)
            {
                UiText.PlainStatusLabel(rect, _load.Error ?? "Preview unavailable", UiTheme.Bad);

            }
            else SandboxPreviewPanel.Draw(rect, ref scroll, _load.Value);
        }
    }
}
