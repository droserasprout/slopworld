using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // A template's catalog projection. The daemon sends snapshots for creation, while
    // the picker only needs the portable defaults to seed the existing agent form controls.
    public class AgentTemplateInfo
    {
        public static readonly string[] FlagNames = { "persistent_tmp", "autostart", "auto_resume" };
        public HashSet<string> SpecifiedFlags = new HashSet<string>();
        public string Name = "";
        // Versions are daemon-owned compare-and-swap tokens. Zero is reserved for a new
        // definition, which lets the same wire model serve capture, duplicate, and edit.
        public long Version;
        public string Description = "";
        public string Command = "";
        public string Cmd = "";
        public List<string> Sandbox = new List<string>();
        public bool PersistentTmp;
        public NetworkMode Network = NetworkMode.Private;
        public bool NetworkSpecified;
        public DnsConfig Dns = DnsConfig.Resolved();
        public bool DnsSpecified;
        public SessionLimits Limits;
        public bool Autostart;
        public bool AutoResume;
        // Keep the daemon's complete snapshots alongside the friendly projection. Editing a
        // name or description must not discard captured preset definitions that are no
        // longer present in the live catalogs.
        public Wire.AgentTemplateDefaults DefaultsSnapshot = new Wire.AgentTemplateDefaults();

        public string DisplayLabel
        {
            get
            {
                return string.IsNullOrEmpty(Description) ? Name : Name + "  -  " + Description;
            }
        }

        public static AgentTemplateInfo FromWire(Wire.AgentTemplate j)
        {
            var d = j.Defaults ?? new Wire.AgentTemplateDefaults();
            var result = new AgentTemplateInfo
            {
                Name = j.Name,
                Version = (long)j.Version,
                Description = j.Description,
                Command = d.Command?.Name ?? "",
                Cmd = d.Cmd,
                Sandbox = d.Sandbox.ToList(),
                PersistentTmp = d.PersistentTmp,
                Network = NetworkModeText.Parse(d.Network),
                NetworkSpecified = d.HasNetwork,
                Dns = DnsConfig.FromWire(d.Dns),
                DnsSpecified = d.Dns != null,
                Limits = SessionLimits.FromWire(d.Limits),
                Autostart = d.Autostart,
                AutoResume = d.AutoResume,
                DefaultsSnapshot = d.Clone(),
            };
            if (d.HasPersistentTmp) result.SpecifiedFlags.Add("persistent_tmp");
            if (d.HasAutostart) result.SpecifiedFlags.Add("autostart");
            if (d.HasAutoResume) result.SpecifiedFlags.Add("auto_resume");
            return result;
        }

        public AgentTemplateInfo Copy() => new AgentTemplateInfo
        {
            SpecifiedFlags = new HashSet<string>(SpecifiedFlags),
            Name = Name,
            Version = Version,
            Description = Description,
            Command = Command,
            Cmd = Cmd,
            Sandbox = new List<string>(Sandbox),
            PersistentTmp = PersistentTmp,
            Network = Network,
            NetworkSpecified = NetworkSpecified,
            Dns = Dns?.Copy() ?? DnsConfig.Resolved(),
            DnsSpecified = DnsSpecified,
            Limits = Limits,
            Autostart = Autostart,
            AutoResume = AutoResume,
            DefaultsSnapshot = DefaultsSnapshot.Clone(),
        };

        // Editors and previews share the same snapshot-first catalogs used when saving.
        public CommandInfo ResolveCommand(string name)
        {
            var captured = DefaultsSnapshot.Command;
            return !string.IsNullOrEmpty(name) && captured?.Name == name
                ? CommandInfo.FromWire(captured) : SessionHub.Instance.Commands.FirstOrDefault(c => c.Name == name);
        }
        public List<PresetInfo> SandboxCatalog() => DefaultsSnapshot.SandboxPresets.Select(PresetInfo.FromWire)
            .Concat(SessionHub.Instance.Presets).GroupBy(p => p.Name).Select(g => g.First()).ToList();
        public Wire.AgentTemplate ToWire(SessionInfo form)
        {
            var d = DefaultsSnapshot.Clone();
            d.Command = null;
            if (!string.IsNullOrEmpty(form.Command))
                d.Command = DefaultsSnapshot.Command?.Name == form.Command ? DefaultsSnapshot.Command.Clone()
                    : (SessionHub.Instance.Commands.FirstOrDefault(c => c.Name == form.Command)?.ToWire() ?? throw new InvalidOperationException("Unknown command: " + form.Command));
            d.ClearCmd();
            if (!string.IsNullOrWhiteSpace(form.Cmd)) d.Cmd = form.Cmd;
            d.Sandbox.Clear(); d.SandboxPresets.Clear();
            var pending = new Queue<string>(form.Sandbox.Concat(d.Command == null ? Enumerable.Empty<string>() : d.Command.Sandbox));
            var names = new HashSet<string>();
            while (pending.Count > 0)
            {
                string name = pending.Dequeue();
                if (!names.Add(name)) continue;
                var preset = DefaultsSnapshot.SandboxPresets.FirstOrDefault(p => p.Name == name)?.Clone()
                    ?? SessionHub.Instance.Presets.FirstOrDefault(p => p.Name == name)?.ToWire()
                    ?? throw new InvalidOperationException("Unknown sandbox preset: " + name);
                d.Sandbox.Add(name); d.SandboxPresets.Add(preset);
                foreach (string dependency in preset.Requires) pending.Enqueue(dependency);
            }
            d.ClearPersistentTmp(); d.ClearAutostart(); d.ClearAutoResume(); d.ClearNetwork();
            if (SpecifiedFlags.Contains("persistent_tmp")) d.PersistentTmp = form.PersistentTmp;
            if (SpecifiedFlags.Contains("autostart")) d.Autostart = form.Autostart;
            if (SpecifiedFlags.Contains("auto_resume")) d.AutoResume = form.AutoResume;
            if (NetworkSpecified) d.Network = NetworkModeText.Name(form.Network);
            d.Dns = DnsSpecified ? form.Dns.ToWire() : null;
            d.Limits = form.Limits.ToWire();
            return new Wire.AgentTemplate
            {
                Name = Name,
                Version = checked((ulong)Version),
                Description = Description,
                Defaults = d
            };
        }

        // Seed a new editor, leaving name and project to creation. Mounts and labels are
        // deliberately not template fields; they are contextual or presentation choices.
        public void ApplyTo(SessionInfo s)
        {
            s.Command = Command ?? "";
            s.CommandPreset = Command ?? "";
            s.Cmd = Cmd ?? "";
            s.Sandbox = new List<string>(Sandbox);
            s.PersistentTmp = PersistentTmp;
            s.Network = Network;
            s.Dns = Dns?.Copy() ?? DnsConfig.Resolved();
            s.Limits = Limits;
            s.Autostart = Autostart;
            s.AutoResume = AutoResume;
        }

    }
}
