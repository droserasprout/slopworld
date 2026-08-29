using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class SessionInfoTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("reads the complete wire shape", ReadsCompleteWireShape);
            yield return ("writes editable fields", WritesEditableFields);
            yield return ("parses unknown state as down", ParsesUnknownStateAsDown);
            yield return ("parses each active state and reports current time", ParsesActiveStates);
        }

        static void ReadsCompleteWireShape()
        {
            var session = SessionInfo.FromJson(JVal.Parse(
                "{" +
                "\"name\":\"agent\",\"project\":\"proj\",\"dir\":\"/work\", " +
                "\"command\":\"claude\",\"command_preset\":\"claude\", " +
                "\"cmd\":\"run --x\",\"sandbox\":[\"home\",\"net\"], " +
                "\"slopworld_md\":true, " +
                "\"persistent_tmp\":true, " +
                "\"agent\":\"/usr/bin/claude\",\"state\":\"working\",\"alive\":true, " +
                "\"network\":\"host\",\"network_override\":\"none\", " +
                "\"dns\":{\"mode\":\"servers\",\"servers\":[\"8.8.8.8\"]}, " +
                "\"dns_override\":{\"mode\":\"resolved\"}, " +
                "\"limits_override\":{\"memory_mb\":512,\"cpu_pct\":75}, " +
                "\"limits\":{\"memory_mb\":1024,\"pids\":64}, " +
                "\"autostart\":true,\"auto_resume\":true,\"auto_resume_pending\":true, " +
                "\"breadcrumb_yolo\":false, " +
                "\"breadcrumbs\":[\"tip one\"],\"breadcrumbs_pending\":true, " +
                "\"ephemeral\":true,\"cols\":120,\"rows\":40, " +
                "\"title\":\"working title\",\"label\":\"manual label\",\"bell\":true, " +
                "\"last_change\":123,\"state_since\":456}"));

            AssertEx.Equal("agent", session.Name, "name");
            AssertEx.Equal("proj", session.Project, "project");
            AssertEx.Equal("/work", session.Dir, "directory");
            AssertEx.Equal("claude", session.Command, "command");
            AssertEx.Equal("claude", session.CommandPreset, "command preset");
            AssertEx.Equal("run --x", session.Cmd, "command override");
            AssertEx.Sequence(new[] { "home", "net" }, session.Sandbox, "sandbox list");
            AssertEx.True(session.SlopworldMd, "SlopWorld manifest");
            AssertEx.True(session.PersistentTmp, "persistent /tmp");
            AssertEx.Equal("/usr/bin/claude", session.Agent, "resolved agent");
            AssertEx.Equal(AgentState.Working, session.State, "state");
            AssertEx.True(session.Alive, "alive");
            AssertEx.Equal(NetworkMode.Host, session.Network, "effective network");
            AssertEx.Equal(NetworkMode.None, session.NetworkOverride.Value,
                           "network override");
            AssertEx.Equal(DnsMode.Servers, session.Dns.Mode, "effective DNS mode");
            AssertEx.Sequence(new[] { "8.8.8.8" }, session.Dns.Servers, "effective DNS servers");
            AssertEx.True(session.DnsOverride.IsResolved, "DNS override");
            AssertEx.Equal(512, session.Limits.MemoryMb.Value, "session memory limit");
            AssertEx.Equal(75, session.Limits.CpuPct.Value, "session CPU limit");
            AssertEx.Equal(1024, session.EffectiveLimits.MemoryMb.Value,
                           "effective memory limit");
            AssertEx.Equal(64, session.EffectiveLimits.Pids.Value, "effective pids limit");
            AssertEx.True(session.Autostart, "autostart");
            AssertEx.True(session.AutoResume, "auto resume");
            AssertEx.True(session.AutoResumePending, "auto resume pending");
            AssertEx.False(session.BreadcrumbYolo, "breadcrumb yolo");
            AssertEx.Sequence(new[] { "tip one" }, session.Breadcrumbs, "breadcrumbs");
            AssertEx.True(session.BreadcrumbsPending, "breadcrumbs pending");
            AssertEx.True(session.Ephemeral, "ephemeral");
            AssertEx.Equal(120, session.Cols, "columns");
            AssertEx.Equal(40, session.Rows, "rows");
            AssertEx.Equal("working title", session.Title, "title");
            AssertEx.Equal("manual label", session.Label, "label");
            AssertEx.True(session.Bell, "bell");
            AssertEx.Equal(123L, session.LastChange, "last change");
            AssertEx.Equal(456L, session.StateSince, "state since");
            AssertEx.False(session.Gone, "alive session is not gone");
        }

        static void WritesEditableFields()
        {
            var session = new SessionInfo
            {
                Name = "agent",
                Project = "proj",
                Command = "claude",
                Cmd = "run --x",
                Sandbox = new List<string> { "home" },
                SlopworldMd = true,
                PersistentTmp = true,
                Breadcrumbs = new List<string> { "tip" },
                Label = "label",
                NetworkOverride = NetworkMode.Host,
                DnsOverride = new DnsConfig
                {
                    Mode = DnsMode.Servers,
                    Servers = new List<string> { "8.8.8.8" },
                },
                Limits = new SessionLimits { MemoryMb = 512 },
                Autostart = true,
                AutoResume = true,
                BreadcrumbYolo = false,
            };
            var json = JVal.Parse(session.ToJson());

            AssertEx.Equal("agent", json["name"].AsString(), "written name");
            AssertEx.Equal("proj", json["project"].AsString(), "written project");
            AssertEx.Equal("run --x", json["cmd"].AsString(), "written command override");
            AssertEx.Equal("home", json["sandbox"][0].AsString(), "written sandbox");
            AssertEx.True(json["slopworld_md"].AsBool(), "written SlopWorld manifest");
            AssertEx.True(json["persistent_tmp"].AsBool(), "written persistent /tmp");
            AssertEx.Equal("tip", json["breadcrumbs"][0].AsString(), "written breadcrumb");
            AssertEx.Equal("label", json["label"].AsString(), "written label");
            AssertEx.Equal("host", json["network"].AsString(), "written network override");
            AssertEx.Equal("8.8.8.8", json["dns"]["servers"][0].AsString(),
                           "written DNS override");
            AssertEx.Equal(512, json["limits"]["memory_mb"].AsInt(), "written memory limit");
            AssertEx.True(json["autostart"].AsBool(), "written autostart");
            AssertEx.True(json["auto_resume"].AsBool(), "written auto resume");
            AssertEx.False(json["breadcrumb_yolo"].AsBool(true), "written breadcrumb yolo");
            AssertEx.True(json["agent"].IsNull, "resolved agent is not written");
        }

        static void ParsesUnknownStateAsDown()
        {
            var session = SessionInfo.FromJson(JVal.Parse(
                "{\"state\":\"future-state\",\"alive\":false}"));

            AssertEx.Equal(AgentState.Down, session.State, "unknown state fallback");
            AssertEx.True(session.Gone, "dead session is gone");
            AssertEx.Equal(0, session.Cols, "missing columns default");
            AssertEx.Equal(0, session.Rows, "missing rows default");
            AssertEx.False(session.AutoResume, "auto resume default");
            AssertEx.False(session.PersistentTmp, "persistent /tmp default");
            AssertEx.True(session.BreadcrumbYolo, "breadcrumb yolo default");
        }

        static void ParsesActiveStates()
        {
            AssertEx.Equal(AgentState.Waiting, SessionInfo.ParseState("waiting"),
                           "waiting state");
            AssertEx.Equal(AgentState.Idle, SessionInfo.ParseState("idle"), "idle state");
            AssertEx.True(SessionInfo.NowMs > 1_000_000_000_000L,
                          "current time is expressed as Unix milliseconds");
        }
    }
}
