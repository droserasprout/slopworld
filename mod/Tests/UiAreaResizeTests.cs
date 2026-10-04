using UnityEngine;
using Verse;

namespace SlopWorld.Tests
{
    static class UiAreaResizeTests
    {
        static readonly Rect Area = new Rect(20f, 30f, 300f, 180f);

        static void Send(UiAreaResize resize, EventType type, bool enabled = true, int button = 0)
        {
            Event.current = new Event
            {
                type = type, rawType = type, button = button,
                mousePosition = new Vector2(Area.xMax - 1f, Area.yMax - 1f),
            };
            resize.Input(Area, "prompt", enabled);
            resize.Complete(Area, enabled);
        }

        public static void AutomaticSizingCapsContentAndDoesNotMutateOnMeasurement()
        {
            var growing = new UiAreaResize(48f, grow: true);
            AssertEx.Equal(48f, growing.MeasuredHeight(12f), "short content keeps the minimum");
            AssertEx.Equal(140f, growing.MeasuredHeight(140f), "content expands the field");
            AssertEx.Equal(240f, growing.MeasuredHeight(900f), "automatic growth is capped");
            AssertEx.Equal(48f, growing.MeasuredHeight(12f), "deleting content shrinks the field");
            AssertEx.Equal(48f, growing.Height, "measurement does not mutate retained geometry");
            var manual = new UiAreaResize(180f);
            AssertEx.Equal(180f, manual.MeasuredHeight(900f), "prompt height stays manual while typing");
            var tallMinimum = new UiAreaResize(300f, minimum: 300f, grow: true);
            AssertEx.Equal(300f, tallMinimum.MeasuredHeight(900f), "automatic cap respects a taller explicit minimum");
            var limited = new UiAreaResize(44f, minimum: 44f, maximum: 100f, grow: true);
            AssertEx.Equal(100f, limited.MeasuredHeight(900f), "explicit maximum also bounds growth");
        }

        public static void DragOverridesGrowthUntilFitToContentRestoresIt()
        {
            var lifetime = new FieldLifetime();
            using (FieldLifetimeScope.Push(lifetime))
            {
                var resize = new UiAreaResize(48f, grow: true);
                GUIUtility.hotControl = 0;
                Send(resize, EventType.MouseDown);
                WindowResizer.Result = new Rect(0f, 0f, 300f, 320f);
                Send(resize, EventType.MouseDrag);
                AssertEx.True(!resize.Automatic, "drag switches to manual sizing");
                AssertEx.Equal(320f, resize.MeasuredHeight(900f), "content cannot override the manual height");
                resize.FitToContent();
                resize.FitToContent();
                AssertEx.Equal(0, GUIUtility.hotControl, "fitting also retires an active gesture");
                AssertEx.Equal(240f, resize.MeasuredHeight(900f), "fitting restores capped automatic growth");
                AssertEx.Equal(48f, resize.MeasuredHeight(12f), "fitting also permits shrinking");
            }
        }

        public static void NativeResultsOnlyChangeHeightAndReleaseSurvivesConsumedEvent()
        {
            var lifetime = new FieldLifetime();
            using (FieldLifetimeScope.Push(lifetime))
            {
                var resize = new UiAreaResize(180f);
                GUIUtility.hotControl = 0;
                Send(resize, EventType.MouseDown);
                AssertEx.True(GUIUtility.hotControl != 0, "corner captures the gesture");
                AssertEx.True(WindowResizer.Compact, "native control uses the compact window grip");
                WindowResizer.Result = new Rect(0f, 0f, 999f, 999f);
                Send(resize, EventType.MouseDrag);
                AssertEx.Equal(600f, resize.Height, "native height is bounded");
                AssertEx.Equal(300f, Area.width, "horizontal drag leaves layout width unchanged");
                WindowResizer.Result = new Rect(0f, 0f, 1f, 1f);
                Send(resize, EventType.MouseUp);
                AssertEx.Equal(48f, resize.Height, "native height respects the minimum");
                AssertEx.Equal(0, GUIUtility.hotControl, "release clears capture even when native code uses the event");
                AssertEx.True(lifetime.CancelResize == null, "release retires the lifetime callback");
            }
        }

        public static void ClosingDisablingAndLostCaptureRetireTheGesture()
        {
            foreach (int reason in new[] { 0, 1, 2, 3 })
            {
                var lifetime = new FieldLifetime();
                using (FieldLifetimeScope.Push(lifetime))
                {
                    var resize = new UiAreaResize(180f);
                    GUIUtility.hotControl = 0;
                    Send(resize, EventType.MouseDown);
                    if (reason == 0) { lifetime.Cancel(); lifetime.Cancel(); }
                    if (reason == 1) Send(resize, EventType.MouseDrag, enabled: false);
                    if (reason == 2) { GUIUtility.hotControl = 42; Send(resize, EventType.MouseDrag); }
                    if (reason == 3) { Input.Held = false; Send(resize, EventType.Repaint); }
                    AssertEx.Equal(reason == 2 ? 42 : 0, GUIUtility.hotControl,
                        "cleanup only releases the area's capture");
                    AssertEx.True(lifetime.CancelResize == null, "cleanup is repeatable and retires callback");
                    AssertEx.Equal(180f, resize.Height, "cancel does not commit a drag");
                }
            }
            GUIUtility.hotControl = 0;
        }

        public static void DisabledAndSecondaryPressesDoNotInvokeNativeInput()
        {
            var resize = new UiAreaResize(180f);
            WindowResizer.InputCalls = 0;
            Send(resize, EventType.MouseDown, enabled: false);
            Send(resize, EventType.MouseDown, button: 1);
            AssertEx.Equal(0, WindowResizer.InputCalls, "only enabled primary corner presses reach native input");
        }

        public static void CapturedConsumedDragAndReleaseStillResizeOutsideTheField()
        {
            var lifetime = new FieldLifetime();
            using (FieldLifetimeScope.Push(lifetime))
            {
                var resize = new UiAreaResize(180f);
                GUIUtility.hotControl = 0;
                // WindowStack can use captured events before the field draws. Unity can
                // also clear their button; the original gesture kind survives in rawType.
                Event.current = new Event
                {
                    type = EventType.Used, rawType = EventType.MouseDown, button = -1,
                    mousePosition = new Vector2(Area.xMax - 1f, Area.yMax - 1f),
                };
                resize.Input(Area, "prompt", true);
                resize.Complete(Area, true);
                AssertEx.True(GUIUtility.hotControl != 0, "used press in the grip captures the gesture");
                AssertEx.Equal(EventType.Used, Event.current.type, "native replay keeps the press consumed");
                Input.Held = true;
                Event.current = new Event
                {
                    type = EventType.Used, rawType = EventType.MouseDrag, button = -1,
                    mousePosition = new Vector2(Area.xMax + 30f, Area.yMax + 64f),
                };
                WindowResizer.Result = new Rect(0f, 0f, 330f, 244f);
                resize.Input(Area, "prompt", true);
                resize.Complete(Area, true);
                AssertEx.Equal(244f, resize.Height, "captured used drag commits native height outside the field");
                AssertEx.True(GUIUtility.hotControl != 0, "used drag retains capture");

                Input.Held = false;
                Event.current = new Event
                {
                    type = EventType.Used, rawType = EventType.MouseUp, button = -1,
                    mousePosition = new Vector2(Area.xMax + 30f, Area.yMax + 80f),
                };
                WindowResizer.Result = new Rect(0f, 0f, 330f, 260f);
                resize.Input(Area, "prompt", true);
                resize.Complete(Area, true);
                AssertEx.Equal(260f, resize.Height, "used release commits the final height");
                AssertEx.Equal(0, GUIUtility.hotControl, "used release clears capture");
                AssertEx.True(lifetime.CancelResize == null, "used release retires the lifetime callback");
            }
        }

        public static void NativeButtonCaptureIsReturnedAndEachPassAllocatesOneGrip()
        {
            var lifetime = new FieldLifetime();
            using (FieldLifetimeScope.Push(lifetime))
            {
                var resize = new UiAreaResize(180f);
                GUIUtility.hotControl = 0;
                WindowResizer.Calls = 0;
                Send(resize, EventType.Layout);
                Send(resize, EventType.Repaint);
                Send(resize, EventType.MouseDown);
                int adapter = GUIUtility.GetControlID("prompt".GetHashCode(), FocusType.Passive);
                AssertEx.Equal(adapter, GUIUtility.hotControl, "native button returns capture to the area adapter");
                Input.Held = true;
                Send(resize, EventType.Layout);
                AssertEx.Equal(EventType.Layout, Event.current.type, "native dispatch preserves the outer layout event");
                Send(resize, EventType.Repaint);
                AssertEx.Equal(adapter, GUIUtility.hotControl, "layout and repaint retain the adapter's capture");
                AssertEx.Equal(5, WindowResizer.Calls, "idle and captured passes allocate exactly one native grip");

                Send(resize, EventType.MouseUp, button: 1);
                AssertEx.Equal(adapter, GUIUtility.hotControl, "a secondary release does not end the primary drag");
                lifetime.Cancel();
                Input.Held = false;
            }
        }
    }
}
