using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The client owns inhibition independently of launcher, daemon, and colony lifetimes.
    static class IdleInhibition
    {
        static readonly object Gate = new object();
        static IdleInhibitionWorker _worker;
        static bool _started;
        static bool _quitting;

        public static void Start()
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer)
                return;
            lock (Gate)
            {
                if (_started)
                    return;
                _started = true;
                Application.quitting += Stop;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
            }
            SetEnabled(Settings.InhibitScreensaver);
        }

        internal static void SetEnabled(bool enabled)
        {
            lock (Gate)
            {
                // Start is called only in an active profile on Linux. Toggling a
                // preference elsewhere must not load native libraries or bypass that gate.
                if (!_started || _quitting)
                    return;
                if (!enabled)
                {
                    _worker?.RequestStop();
                    _worker = null;
                }
                else if (_worker == null)
                {
                    _worker = new IdleInhibitionWorker(
                        () => LinuxIdleLease.Acquire(),
                        e =>
                            Log.Warning(
                                "[SlopWorld] could not inhibit screen dimming: " + e.Message
                            )
                    );
                }
            }
        }

        static void Stop()
        {
            IdleInhibitionWorker worker;
            lock (Gate)
            {
                _quitting = true;
                worker = _worker;
                _worker = null;
            }
            worker?.Dispose();
        }
    }
}
