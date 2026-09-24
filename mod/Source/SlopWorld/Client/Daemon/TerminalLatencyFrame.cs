using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace SlopWorld
{
    // A lifecycle hook only: no screenshots, readbacks or forced GPU synchronization.
    internal sealed class TerminalLatencyFrame : MonoBehaviour
    {
        static StreamWriter _output;
        static bool _failed;

        // Dedicated file avoids Verse's global message cap during input storms.
        // One main-thread writer flushes once per frame after the timed endpoint.
        internal static void WriteRecord(string line)
        {
            if (_failed || _output == null) return;
            try { _output.WriteLine(line); }
            catch (Exception error) { Fail(error); }
        }
        static void Fail(Exception error)
        {
            _failed = true;
            Verse.Log.Error("[SlopWorld] trace file failed: " + error.Message);
        }
        static void FlushFile()
        {
            if (_failed || _output == null) return;
            try { _output.Flush(); }
            catch (Exception error) { Fail(error); }
        }
        internal static void Install()
        {
            if (!TerminalLatency.Enabled && !PerfTrace.Enabled) return;
            try
            {
                string path = Path.Combine(Verse.GenFilePaths.SaveDataFolderPath, "SlopWorld-trace.log");
                _output = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
                _output.WriteLine("# SlopWorld trace " + DateTime.UtcNow.ToString("O"));
                _output.Flush();
                Verse.Log.Message("[SlopWorld] diagnostic trace: " + path);
            }
            catch (Exception error) { Fail(error); }
            TerminalLatency.Write = WriteRecord;
            var owner = new GameObject("SlopWorld latency timeline");
            UnityEngine.Object.DontDestroyOnLoad(owner);
            owner.AddComponent<TerminalLatencyFrame>();
        }
        IEnumerator Start()
        {
            var end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                if (TerminalLatency.Enabled)
                {
                    TerminalLatency.Timeline.EndFrame(Time.frameCount);
                    TerminalLatency.Flush();
                }
                FlushFile();
            }
        }
        void Update()
        {
            if (TerminalLatency.Enabled) TerminalLatency.Timeline.Expire();
        }
        void OnApplicationQuit()
        {
            if (TerminalLatency.Enabled)
            {
                TerminalLatency.Timeline.Reset();
                TerminalLatency.Flush();
            }
            FlushFile();
            try { _output?.Dispose(); }
            catch (Exception error) { if (!_failed) Fail(error); }
            _output = null;
        }
    }
}
