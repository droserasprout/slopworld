using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class SessionInfoTests
    {
        public static void PreservesUnsignedReaderLine()
        {
            foreach (uint line in new[] { 2147483648U, uint.MaxValue })
            {
                var session = SessionInfo.FromWire(new Wire.SessionView
                {
                    Launch = new Wire.SessionLaunchView(),
                    Runtime = new Wire.SessionRuntimeView(),
                    Worker = new Wire.SessionWorkerView(),
                    Reader = new Wire.SessionReaderView { Line = line },
                });
                AssertEx.Equal(line, session.ReaderLine, "reader line preserves wire range");
                AssertEx.True(PagerCommands.FileCommand("less", "less", "file", session.ReaderLine)
                    .Contains("+" + line), "restored command preserves unsigned line");
            }
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("reads the complete wire shape", ReadsCompleteWireShape);
            yield return ("writes editable fields", WritesEditableFields);
            yield return ("parses unknown state as down", ParsesUnknownStateAsDown);
            yield return ("parses each active state and reports current time", ParsesActiveStates);
        }

        static void ReadsCompleteWireShape()
        {
            var session = SessionInfo.FromWire(ProtobufFixtures.Read<Wire.SessionView>(JVal.Parse(
                """
                {
                  "name": "agent",
                  "project": "proj",
                  "worktree": "tree-id",
                  "worktree_name": "feature",
                  "intent": "search",
                  "dir": "/work",
                  "ephemeral": true,
                  "host": true,
                  "label": "manual label",
                  "reader": {
                    "path": "/work/file.rs",
                    "key": "file-key",
                    "scope": "proj/main",
                    "pinned": true,
                    "line": 17
                  },
                  "runtime": {
                    "state": "working",
                    "alive": true,
                    "auto_resume_pending": true,
                    "process_running": true,
                    "cols": 120,
                    "rows": 40,
                    "title": "working title",
                    "bell": true,
                    "run_id": 9,
                    "last_change": 123,
                    "state_since": 456
                  },
                  "launch": {
                    "command": "claude",
                    "command_preset": "claude",
                    "cmd": "run --x",
                    "args": "--extra",
                    "sandbox": [
                      "home",
                      "net"
                    ],
                    "persistent_tmp": true,
                    "mounts": [{ "from": "shared", "to": "/mnt/shared", "mode": "ro" }],
                    "agent": "/usr/bin/claude",
                    "network": "host",
                    "dns": {
                      "mode": "servers",
                      "servers": [
                        "8.8.8.8"
                      ]
                    },
                    "limits": {
                      "memory_mb": 1024,
                      "pids": 64
                    },
                    "autostart": true,
                    "auto_resume": true
                  },
                  "worker": {
                    "enabled": true,
                    "parent": "caller",
                    "task_id": "task-7",
                    "durable": true
                  }
                }
                """)));

            AssertEx.Equal("agent", session.Name, "name");
            AssertEx.Equal("proj", session.Project, "project");
            AssertEx.Equal("tree-id", session.Worktree, "worktree identity");
            AssertEx.Equal("feature", session.WorktreeName, "worktree display name");
            AssertEx.Equal("search", session.Intent, "reader intent");
            AssertEx.Equal(1, session.Mounts.Count, "mount count");
            AssertEx.Equal("shared", session.Mounts[0].From, "mount source");
            AssertEx.Equal("/mnt/shared", session.Mounts[0].To, "mount destination");
            AssertEx.Equal(MountMode.Ro, session.Mounts[0].Mode, "mount mode");
            AssertEx.Equal("/work", session.Dir, "directory");
            AssertEx.Equal("claude", session.Command, "command");
            AssertEx.Equal("claude", session.CommandPreset, "command preset");
            AssertEx.Equal("run --x", session.Cmd, "command override");
            AssertEx.Sequence(new[] { "home", "net" }, session.Sandbox, "sandbox list");
            AssertEx.True(session.PersistentTmp, "persistent /tmp");
            AssertEx.Equal("/usr/bin/claude", session.Agent, "resolved agent");
            AssertEx.Equal(AgentState.Working, session.State, "state");
            AssertEx.True(session.Alive, "alive");
            AssertEx.Equal(NetworkMode.Host, session.Network, "effective network");
            AssertEx.Equal(DnsMode.Servers, session.Dns.Mode, "effective DNS mode");
            AssertEx.Sequence(new[] { "8.8.8.8" }, session.Dns.Servers, "effective DNS servers");
            AssertEx.Equal(1024U, session.Limits.MemoryMb.Value, "agent memory limit");
            AssertEx.Equal(64U, session.Limits.Pids.Value, "agent process limit");
            AssertEx.True(session.Autostart, "autostart");
            AssertEx.True(session.AutoResume, "auto resume");
            AssertEx.True(session.AutoResumePending, "auto resume pending");
            AssertEx.True(session.Worker, "task worker");
            AssertEx.Equal("caller", session.Parent, "worker parent");
            AssertEx.Equal("task-7", session.TaskId, "worker task id");
            AssertEx.True(session.Durable, "durable worker");
            AssertEx.True(session.Ephemeral, "ephemeral");
            AssertEx.True(session.Host, "host terminal");
            AssertEx.True(session.ProcessRunning, "foreground host process");
            AssertEx.Equal(120, session.Cols, "columns");
            AssertEx.Equal(40, session.Rows, "rows");
            AssertEx.Equal("working title", session.Title, "title");
            AssertEx.Equal("manual label", session.Label, "label");
            AssertEx.True(session.Bell, "bell");
            AssertEx.Equal(9L, session.RunId, "run identity");
            AssertEx.Equal(123L, session.LastChange, "last change");
            AssertEx.Equal(456L, session.StateSince, "state since");
            AssertEx.Equal("/work/file.rs", session.ReaderPath, "reader path");
            AssertEx.Equal("--extra", session.Args, "extra arguments from launch view");
            AssertEx.Equal("file-key", session.ReaderKey, "reader key");
            AssertEx.Equal("proj/main", session.ReaderScope, "reader scope");
            AssertEx.True(session.ReaderPinned, "reader pinned");
            AssertEx.Equal(17U, session.ReaderLine, "reader line");
            AssertEx.False(session.Gone, "alive session is not gone");
        }

        static void WritesEditableFields()
        {
            var session = new SessionInfo
            {
                Name = "agent",
                Project = "proj",
                Worktree = "tree-id",
                Command = "claude",
                Cmd = "run --x",
                Args = "--extra",
                Sandbox = new List<string> { "home" },
                PersistentTmp = true,
                Label = "label",
                Network = NetworkMode.Host,
                Dns = new DnsConfig
                {
                    Mode = DnsMode.Servers,
                    Servers = new List<string> { "8.8.8.8" },
                },
                Limits = new SessionLimits { MemoryMb = 512 },
                Autostart = true,
                AutoResume = true,
            };
            var json = JVal.Parse(session.ToJson());

            AssertEx.Equal("agent", json["name"].AsString(), "written name");
            AssertEx.Equal("proj", json["project"].AsString(), "written project");
            AssertEx.Equal("claude", json["command"].AsString(), "written command preset");
            AssertEx.Equal("tree-id", json["worktree"].AsString(), "written worktree identity");
            AssertEx.Equal("run --x", json["cmd"].AsString(), "written command override");
            AssertEx.Equal("--extra", json["args"].AsString(), "written extra arguments");
            AssertEx.Equal("home", json["sandbox"][0].AsString(), "written sandbox");
            AssertEx.True(json["persistent_tmp"].AsBool(), "written persistent /tmp");
            AssertEx.Equal("label", json["label"].AsString(), "written label");
            AssertEx.Equal("host", json["network"].AsString(), "written network");
            AssertEx.Equal("8.8.8.8", json["dns"]["servers"][0].AsString(),
                           "written DNS override");
            AssertEx.Equal(512, json["limits"]["memory_mb"].AsInt(), "written memory limit");
            AssertEx.True(json["autostart"].AsBool(), "written autostart");
            AssertEx.True(json["auto_resume"].AsBool(), "written auto resume");
            AssertEx.True(json["network_override"].IsNull, "removed network override is not written");
            AssertEx.True(json["breadcrumbs"].IsNull, "removed breadcrumb selections are not written");
            AssertEx.True(json["agent"].IsNull, "resolved agent is not written");
        }

        static void ParsesUnknownStateAsDown()
        {
            var session = SessionInfo.FromWire(ProtobufFixtures.Read<Wire.SessionView>(JVal.Parse(
                "{\"launch\":{},\"worker\":{},\"reader\":{},\"runtime\":{\"state\":\"future-state\",\"alive\":false}}")));

            AssertEx.Equal(AgentState.Down, session.State, "unknown state fallback");
            AssertEx.True(session.Gone, "dead session is gone");
            AssertEx.Equal(0, session.Cols, "missing columns default");
            AssertEx.Equal(0, session.Rows, "missing rows default");
            AssertEx.False(session.AutoResume, "auto resume default");
            AssertEx.False(session.PersistentTmp, "persistent /tmp default");
            AssertEx.False(session.ProcessRunning, "foreground host process default");
            AssertEx.True(session.Dns.IsResolved, "DNS defaults to system resolver");
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
