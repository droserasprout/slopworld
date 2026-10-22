using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Own user-requested exit from GM and the OS. SaveCoordinator owns the final save;
    // Radio owns playback shutdown. Internal restart/profile shutdowns bypass this flow.
    public static class QuitInterceptor
    {
        enum Phase
        {
            Idle,
            Pending,
            Confirming,
            Preparing,
            Stopping,
            Failed,
            Ready,
            ShuttingDown,
        }

        static Phase _phase;
        static Window _notice;
        static string _error;
        static int _attempt;
        static int? _stopGeneration;

        public static void Register() => Application.wantsToQuit += OnWantsToQuit;

        public static void Request()
        {
            if (_phase == Phase.Idle)
                _phase = Phase.Pending;
        }

        // Unity requires a synchronous answer. Defer UI and I/O to Root.Update.
        static bool OnWantsToQuit()
        {
            if (SaveCoordinator.ConsumeProgrammaticShutdown() || _phase == Phase.ShuttingDown)
                return true;
            Request();
            return false;
        }

        public static void Check()
        {
            if (_phase == Phase.Confirming || _phase == Phase.Failed)
            {
                if (_notice != null && Find.WindowStack?.IsOpen(_notice) != true)
                {
                    // Escape or a scene transition cancels this attempt. Old callbacks
                    // and dialog actions must not resume a later quit request.
                    _notice = null;
                    _attempt++;
                    _phase = Phase.Idle;
                }
                else if (_phase == Phase.Failed && _notice == null)
                    ShowFailure();
                return;
            }

            if (_phase == Phase.Pending)
            {
                if (Settings.S.askOnExit)
                    ShowConfirmation();
                else
                    PrepareExit();
            }
            if (_phase == Phase.Ready)
            {
                // Hub updates can replace the connection after the final HTTP completion.
                // Keep its acknowledgement tied to that connection through shutdown.
                var hub = SessionHub.Instance;
                if (_stopGeneration.HasValue && !ConnectionMatches(hub, _stopGeneration.Value))
                {
                    Fail("The daemon connection changed before shutdown.");
                    return;
                }
                _phase = Phase.ShuttingDown;
                Root.Shutdown();
            }
        }

        static void ShowConfirmation()
        {
            if (Find.WindowStack == null)
                return;
            _phase = Phase.Confirming;
            var actions = new List<string>();
            if (Settings.S.radioStopOnExit)
                actions.Add("Radio will stop.");
            if (Settings.S.stopSessionsOnExit)
            {
                var names = ActiveSessionNames(SessionHub.Instance)
                    .OrderBy(name => name, System.StringComparer.Ordinal)
                    .ToArray();
                actions.Add(
                    names.Length == 0
                        ? "No active sessions to stop."
                        : $"Active sessions will stop ({names.Length}):\n"
                            + string.Join("\n", names.Select(name => "• " + name))
                );
            }
            else
                actions.Add("Active sessions will keep running.");
            Window notice = null;
            bool dontAskAgain = false;
            notice = AlertDialog.Create(
                "Quit SlopWorld?",
                string.Join("\n", actions),
                "Quit",
                () =>
                {
                    if (_phase != Phase.Confirming || _notice != notice)
                        return;
                    if (dontAskAgain)
                        DisableConfirmation();
                    PrepareExit();
                },
                "Cancel",
                checkboxLabel: "Don't ask again",
                checkboxChanged: value => dontAskAgain = value
            );
            _notice = notice;
            Find.WindowStack.Add(notice);
        }

        static void DisableConfirmation()
        {
            Settings.S.askOnExit = false;
            Settings.S.MarkDirty();
            try
            {
                // Shutdown may finish before the timed profile flush gets another frame.
                Settings.S.Write();
            }
            catch (System.Exception error)
            {
                Log.Error("[SlopWorld] could not save quit preference: " + error);
            }
        }

        static void PrepareExit()
        {
            _notice = null;
            _stopGeneration = null;
            int attempt = ++_attempt;
            if (!Settings.S.stopSessionsOnExit)
            {
                _phase = Phase.Ready;
                return;
            }

            _phase = Phase.Preparing;
            var hub = SessionHub.Instance;
            int generation = hub.ConnectionGeneration;
            _stopGeneration = generation;
            if (!hub.Online)
            {
                Fail("The daemon is offline. Active sessions could not be stopped.");
                return;
            }

            // Refresh before taking an owned snapshot. New sessions after this snapshot
            // are outside this exit attempt; do not keep chasing incoming work.
            hub.SessionStore.Refresh(
                () =>
                {
                    if (_attempt != attempt || _phase != Phase.Preparing)
                        return;
                    if (!ConnectionMatches(hub, generation))
                    {
                        Fail("The daemon connection changed before sessions could be stopped.");
                        return;
                    }
                    StopSessions(hub, generation, attempt, ActiveSessionNames(hub).ToArray());
                },
                error =>
                {
                    if (_attempt == attempt && _phase == Phase.Preparing)
                        Fail("Could not refresh active sessions: " + error);
                }
            );
        }

        static IEnumerable<string> ActiveSessionNames(SessionHub hub) =>
            hub.SessionStore.Sessions.Where(s => s.Alive).Select(s => s.Name);

        static bool ConnectionMatches(SessionHub hub, int generation) =>
            hub.Online && hub.ConnectionGeneration == generation;

        static void StopSessions(SessionHub hub, int generation, int attempt, string[] names)
        {
            _phase = Phase.Stopping;
            int remaining = names.Length;
            var errors = new List<string>();
            if (remaining == 0)
            {
                _phase = Phase.Ready;
                return;
            }

            void Settled(string name, string error)
            {
                if (_attempt != attempt || _phase != Phase.Stopping)
                    return;
                if (!ConnectionMatches(hub, generation))
                {
                    Fail("The daemon connection changed while stopping sessions.");
                    return;
                }
                if (error != null)
                    errors.Add(name + ": " + error);
                if (--remaining != 0)
                    return;
                // Reload once for the batch, including partial failure.
                hub.SessionStore.Refresh();
                if (errors.Count > 0)
                    Fail(string.Join("\n", errors));
                else
                    _phase = Phase.Ready;
            }

            foreach (string name in names)
            {
                if (_phase != Phase.Stopping)
                    break;
                hub.SessionStore.Stop(
                    name,
                    error => Settled(name, error),
                    () => Settled(name, null)
                );
            }
        }

        static void Fail(string error)
        {
            _error = error;
            _phase = Phase.Failed;
            _notice = null;
        }

        static void ShowFailure()
        {
            if (Find.WindowStack == null)
                return;
            Window notice = null;
            notice = AlertDialog.Create(
                "Could not stop active sessions",
                _error + "\n\nRetry, quit anyway, or press Escape to stay in SlopWorld.",
                "Retry",
                () =>
                {
                    if (_phase == Phase.Failed && _notice == notice)
                        PrepareExit();
                },
                "Quit anyway",
                () =>
                {
                    if (_phase != Phase.Failed || _notice != notice)
                        return;
                    _notice = null;
                    _stopGeneration = null;
                    _phase = Phase.Ready;
                }
            );
            _notice = notice;
            Find.WindowStack.Add(notice);
        }
    }
}
