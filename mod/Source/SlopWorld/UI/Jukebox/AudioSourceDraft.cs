using System;
using System.Collections.Generic;
using System.Globalization;

namespace SlopWorld
{
    // Validate a detached candidate so a later invalid row cannot partially normalize the form.
    internal static class AudioSourceDraft
    {
        internal static bool TryPrepare(JukeboxPresetInfo source, IList<string> rawRates,
            out JukeboxPresetInfo candidate, out string error)
        {
            candidate = null;
            error = null;
            var draft = source.Copy();
            draft.Name = (draft.Name ?? "").Trim();
            if (draft.Name.Length == 0) { error = "Enter a source name."; return false; }
            if (draft.Streams.Count == 0 || rawRates.Count != draft.Streams.Count)
            { error = "Add a bitrate preset."; return false; }

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stream in draft.Streams)
                if (!string.IsNullOrEmpty(stream.Key) && !keys.Add(stream.Key))
                { error = "Each stream key must be unique."; return false; }

            var rates = new HashSet<uint>();
            for (int i = 0; i < draft.Streams.Count; i++)
            {
                var stream = draft.Streams[i];
                if (!uint.TryParse((rawRates[i] ?? "").Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out uint rate) || rate == 0)
                { error = "Enter a positive bitrate for every preset."; return false; }
                string url = (stream.Url ?? "").Trim();
                int schemeLength = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? 5 :
                    url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 4 : 0;
                if (schemeLength == 0)
                { error = "Every stream URL must start with http:// or https://."; return false; }
                if (!rates.Add(rate))
                { error = "Each bitrate preset must be unique."; return false; }

                stream.Rate = rate;
                stream.Url = url.Substring(0, schemeLength).ToLowerInvariant() + url.Substring(schemeLength);
                if (string.IsNullOrEmpty(stream.Key))
                {
                    string stem = draft.Id + "-" + rate.ToString(CultureInfo.InvariantCulture);
                    string key = stem;
                    int suffix = 2;
                    while (!keys.Add(key)) key = stem + "-" + suffix++;
                    stream.Key = key;
                }
            }
            if (!rates.Contains(draft.DefaultRate)) draft.DefaultRate = draft.Streams[0].Rate;
            candidate = draft;
            return true;
        }
    }
}
