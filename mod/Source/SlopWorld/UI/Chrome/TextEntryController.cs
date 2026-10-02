using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // One owner for native text-entry identity and all edits that arrive after the IMGUI
    // pass. UiText keeps the public field shapes and frame policy. This controller owns the
    // Unity editor, clipboard requests and focus-sensitive event choreography.
    internal static class TextEntryController
    {
        sealed class PendingFieldEdit
        {
            public readonly string Name;
            public readonly int ControlId;
            public readonly FieldLifetime Lifetime;
            public readonly Action<TextEditor> Apply;

            public PendingFieldEdit(string name, int controlId, FieldLifetime lifetime,
                                    Action<TextEditor> apply)
            {
                Name = name;
                ControlId = controlId;
                Lifetime = lifetime;
                Apply = apply;
            }
        }

        static readonly Queue<PendingFieldEdit> PendingEdits =
            new Queue<PendingFieldEdit>();

        delegate void NativeTextField(Rect rect, int id, GUIContent content, bool multiline,
                                      int maxLength, GUIStyle style);

        static readonly NativeTextField DrawNativeTextField = (NativeTextField)Delegate.CreateDelegate(
            typeof(NativeTextField), typeof(GUI).GetMethod("DoTextField",
                BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(Rect), typeof(int), typeof(GUIContent), typeof(bool),
                        typeof(int), typeof(GUIStyle) }, null));

        // Unity's text controls consume function keys while focused, before the game or the
        // terminal can dispatch them. Release only this field's focus so bare F-keys reach
        // the mod's chrome and shifted F-keys remain available to the terminal. The caller
        // skips drawing the native control for that event, so it cannot consume the key again.
        // Ordinary typing and function keys in unrelated controls are unaffected.
        public static bool ReleaseFunctionKeyFocus(string name)
        {
            if (GUI.GetNameOfFocusedControl() != name) return false;

            var e = Event.current;
            if (e == null || e.rawType != EventType.KeyDown
                || e.keyCode < KeyCode.F1 || e.keyCode > KeyCode.F15)
                return false;

            GUI.FocusControl(null);
            return true;
        }

        struct NativePreparation
        {
            public Event Event;
            public GUIStyle Style;
            public int ControlId;
            public TextEditor FieldEditor;
            public TextFieldSelection.PrepareResult Selection;
            public bool MouseDown, Replay, KeepSelection;
            public int Button, Cursor, Select;
            public EventType OldType;
        }

        public static string Draw(Rect r, string text, bool area, bool focused, string name,
                                  bool readOnly = false)
        {
            string source = text ?? "";
            var wasColor = GUI.color;
            var lifetime = FieldLifetimeScope.Current;
            NativePreparation input = default;
            bool prepared = false;
            try
            {
                PruneDeadEdits();
                if (wasColor == Color.white)
                    GUI.color = focused || Mouse.IsOver(r) ? UiText.Lead : UiText.Name;
                input = PrepareNative(r, source, area, name, lifetime);
                prepared = true;
                // Replay only after preparation returns, so finally always owns restoration.
                if (input.Replay)
                {
                    var raw = UiEvent.RawType(input.Event);
                    input.Event.type = raw == EventType.ContextClick ? EventType.MouseDown : raw;
                }
                var content = new GUIContent(source);
                DrawNativeTextField(r, input.ControlId, content, area, -1, input.Style);
                var editor = ReconcileEditor(name, source, readOnly, input, lifetime);
                string result = editor == null ? content.text : readOnly ? source : editor.text;
                if (!readOnly) FieldFocusScope.Register(name, input.ControlId, r);
                FinishDraw(r, name, source, area, readOnly, input, editor, lifetime);
                return result;
            }
            finally
            {
                // A native Use() must stay Used so lower layers cannot replay the click.
                if (prepared && input.Replay && input.Event.type != EventType.Used)
                    input.Event.type = input.OldType;
                GUI.color = wasColor;
            }
        }

        static NativePreparation PrepareNative(Rect r, string source, bool area, string name,
                                               FieldLifetime lifetime)
        {
            var e = Event.current;
            var input = new NativePreparation { Event = e, Button = -1 };
            input.MouseDown = e != null && r.Contains(e.mousePosition) && MouseDown(e, out input.Button);
            input.Style = Bare(area ? Verse.Text.CurTextAreaStyle : Verse.Text.CurTextFieldStyle, area);
            // Bind reconciliation to this exact control, never another focused row's editor.
            input.ControlId = GUIUtility.GetControlID(name.GetHashCode(), FocusType.Keyboard, r);
            input.FieldEditor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), input.ControlId);
            var focusedEditor = GUIUtility.keyboardControl == input.ControlId ? input.FieldEditor : null;
            input.Selection = TextFieldSelection.Prepare(r, name, source, input.Style, focusedEditor, lifetime);
            input.Replay = (input.Selection == TextFieldSelection.PrepareResult.None ||
                input.Selection == TextFieldSelection.PrepareResult.TrackedMouseDown) &&
                input.MouseDown && (e.type == EventType.Used || e.type == EventType.ContextClick);
            if (!input.Replay && input.Selection == TextFieldSelection.PrepareResult.None)
                input.Replay = TextFieldSelection.ShouldReplay(name, e, lifetime);
            input.OldType = input.Replay ? e.type : EventType.Ignore;
            input.KeepSelection = input.MouseDown && input.Button == 1 && focusedEditor != null &&
                focusedEditor.IsOverSelection(e.mousePosition);
            if (input.KeepSelection)
            {
                input.Cursor = focusedEditor.cursorIndex;
                input.Select = focusedEditor.selectIndex;
            }
            return input;
        }

        static TextEditor ReconcileEditor(string name, string source, bool readOnly,
                                          NativePreparation input, FieldLifetime lifetime)
        {
            var editor = GUIUtility.keyboardControl == input.ControlId ? input.FieldEditor : null;
            if (editor == null) return null;
            if (input.KeepSelection)
            {
                editor.cursorIndex = input.Cursor;
                editor.selectIndex = input.Select;
            }
            ApplyPendingEdits(name, editor, lifetime);
            if (readOnly && editor.text != source)
            {
                int cursor = editor.cursorIndex, select = editor.selectIndex;
                editor.text = source;
                editor.cursorIndex = Mathf.Clamp(cursor, 0, source.Length);
                editor.selectIndex = Mathf.Clamp(select, 0, source.Length);
            }
            return editor;
        }

        static void FinishDraw(Rect r, string name, string source, bool area, bool readOnly,
                               NativePreparation input, TextEditor editor, FieldLifetime lifetime)
        {
            if (input.Selection == TextFieldSelection.PrepareResult.MultiClick)
                TextFieldSelection.FinishMultiClick(name, editor, lifetime);
            if (input.Selection == TextFieldSelection.PrepareResult.None ||
                input.Selection == TextFieldSelection.PrepareResult.SelectionDrag)
                TextFieldSelection.Handle(r, name, source, input.Style, editor, lifetime);
            if (MouseUp(input.Event)) CopyPrimarySelection(editor);
            if (input.MouseDown && input.Button == 2 && !readOnly)
            {
                RequestPaste(name, area, primary: true, lifetime: lifetime);
                input.Event.Use();
            }
            else if (input.MouseDown && input.Button == 1)
            {
                OpenContextMenu(name, area, editor, lifetime, readOnly);
                input.Event.Use();
            }
        }

        // Prune independently of focus/name so closed fields cannot retain clipboard data.
        static void PruneDeadEdits()
        {
            int count = PendingEdits.Count;
            for (int i = 0; i < count; i++)
            {
                var edit = PendingEdits.Dequeue();
                if (edit.Lifetime != null && edit.Lifetime.Alive) PendingEdits.Enqueue(edit);
            }
        }

        static bool MouseDown(Event e, out int button)
        {
            button = e == null ? -1 : e.button;
            if (e == null) return false;

            var type = UiEvent.RawType(e);
            if (type == EventType.ContextClick)
            {
                button = 1;
                return true;
            }
            return type == EventType.MouseDown;
        }

        static bool MouseUp(Event e)
        {
            // A consumed IMGUI mouse-up can report button -1 while rawType still preserves
            // the original left-button event. Do not lose PRIMARY publication in that case.
            // still reject the right/middle buttons when Unity leaves their button intact.
            if (e == null || e.button > 0) return false;
            return UiEvent.RawType(e) == EventType.MouseUp;
        }

        public static TextEditor CurrentEditor(string name)
        {
            if (GUI.GetNameOfFocusedControl() != name) return null;
            int id = GUIUtility.keyboardControl;
            return id == 0
                ? null
                : GUIUtility.QueryStateObject(typeof(TextEditor), id) as TextEditor;
        }

        static void ApplyPendingEdits(string name, TextEditor editor, FieldLifetime lifetime)
        {
            if (editor == null || PendingEdits.Count == 0) return;

            if (GUI.GetNameOfFocusedControl() != name) return;
            var keep = new Queue<PendingFieldEdit>();
            while (PendingEdits.Count > 0)
            {
                var edit = PendingEdits.Dequeue();
                if (edit.Name != name)
                {
                    keep.Enqueue(edit);
                    continue;
                }

                // The original field/window has gone. A control with the same string name
                // must never inherit its delayed clipboard operation.
                if (edit.Lifetime == null || !edit.Lifetime.Alive ||
                    edit.ControlId != GUIUtility.keyboardControl ||
                    edit.Lifetime != lifetime)
                    continue;

                edit.Apply(editor);
                GUI.changed = true;
            }

            while (keep.Count > 0) PendingEdits.Enqueue(keep.Dequeue());
        }

        static void QueueEdit(string name, int controlId, Action<TextEditor> apply,
                              FieldLifetime lifetime = null)
        {
            PruneDeadEdits();
            if (string.IsNullOrEmpty(name) || controlId == 0 || apply == null) return;
            var owner = lifetime ?? FieldLifetimeScope.Current;
            if (!owner.Alive) return;
            PendingEdits.Enqueue(new PendingFieldEdit(name, controlId, owner, apply));
        }

        static string SingleLinePaste(string text, bool area)
        {
            if (area || string.IsNullOrEmpty(text)) return text ?? "";
            return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }

        static void CopyPrimarySelection(TextEditor editor)
        {
            // Like the terminal, a completed mouse selection becomes the host PRIMARY
            // selection. Keep it separate from the ordinary Copy action and CLIPBOARD.
            string selected = editor == null ? null : editor.SelectedText;
            if (!string.IsNullOrEmpty(selected)) DaemonClipboard.CopyPrimary(selected);
        }

        static void QueuePaste(string name, int controlId, string text, bool area,
                               FieldLifetime lifetime = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            string insert = SingleLinePaste(text, area);
            QueueEdit(name, controlId, editor => editor.ReplaceSelection(insert), lifetime);
        }

        static void RequestPaste(string name, bool area, bool primary,
                                 FieldLifetime lifetime = null)
        {
            if (string.IsNullOrEmpty(name)) return;
            int controlId = GUIUtility.keyboardControl;
            if (controlId == 0 || GUI.GetNameOfFocusedControl() != name) return;
            RequestPaste(name, controlId, area, primary,
                lifetime ?? FieldLifetimeScope.Current);
        }

        static void RequestPaste(string name, int controlId, bool area, bool primary,
                                 FieldLifetime lifetime = null)
        {
            var owner = lifetime ?? FieldLifetimeScope.Current;
            if (!owner.Alive) return;

            if (!SessionHub.Instance.Capabilities.Clipboard)
            {
                if (!primary)
                {
                    // The daemon is unavailable in sidecar mode. Unity's local buffer is the
                    // only ordinary clipboard surface the game can access there.
                    QueuePaste(name, controlId, GUIUtility.systemCopyBuffer, area, owner);
                }
                return;
            }

            string path = primary ? WireProtocol.Routes.ClipboardPrimaryText : WireProtocol.Routes.ClipboardText;
            DaemonClient.Get<Wire.TextResult>(path,
                j => QueuePaste(name, controlId, j.Text, area, owner),
                _ =>
                {
                    if (!primary)
                        QueuePaste(name, controlId, GUIUtility.systemCopyBuffer, area, owner);
                });
        }

        static void OpenContextMenu(string name, bool area, TextEditor editor,
                                    FieldLifetime lifetime, bool readOnly = false)
        {
            if (string.IsNullOrEmpty(name)) return;
            int controlId = GUIUtility.keyboardControl;
            if (controlId == 0) return;
            if (lifetime == null || !lifetime.Alive) return;

            string selected = editor?.SelectedText ?? "";
            var options = new List<FloatMenuOption>();
            var availability = new SelectionCommandAvailability(
                selected.Length > 0, !readOnly, true, selected.Length > 0);
            SelectionCommands.Add(
                options, availability,
                () => DaemonClipboard.Copy(selected),
                !readOnly ? (Action)(() => RequestPaste(name, controlId, area, false, lifetime)) : null,
                () => QueueEdit(name, controlId, e => e.SelectAll(), lifetime),
                !readOnly ? (Action)(() =>
                {
                    DaemonClipboard.Copy(selected);
                    QueueEdit(name, controlId, e => e.DeleteSelection(), lifetime);
                }) : null);

            UiMenu.Open(options);
        }

        static readonly Dictionary<GUIStyle, GUIStyle> FieldStyles = new Dictionary<GUIStyle, GUIStyle>();
        static readonly Dictionary<GUIStyle, GUIStyle> AreaStyles = new Dictionary<GUIStyle, GUIStyle>();
        static int _styleRevision = -1;

        static GUIStyle Bare(GUIStyle of, bool area)
        {
            // GUIStyle and RectOffset allocate native objects. Rebuilding them for every
            // field on every Layout/input/repaint pass turns scrolling forms into native
            // allocation storms even when wheel-only page drawing has been bypassed.
            int revision = UiMetrics.Revision;
            if (_styleRevision != revision)
            {
                FieldStyles.Clear();
                AreaStyles.Clear();
                _styleRevision = revision;
            }
            var styles = area ? AreaStyles : FieldStyles;
            if (styles.TryGetValue(of, out var cached) && cached.font == of.font &&
                cached.fontSize == of.fontSize && cached.fontStyle == of.fontStyle)
                return cached;
            var style = new GUIStyle(of);

            // Vanilla's skin can leave an active/on-state texture behind even after the
            // ordinary states are cleared. Strip every state so Slab is the only owner of
            // field chrome, just as it is for buttons, checkboxes and context menus.
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            style.onNormal.background = null;
            style.onHover.background = null;
            style.onActive.background = null;
            style.onFocused.background = null;

            // The outer rect already supplies the rhythm. Vanilla text-entry padding and
            // offsets otherwise make the same font sit differently in fields and menu rows.
            style.border = new RectOffset();
            style.margin = new RectOffset();
            style.overflow = new RectOffset();
            style.padding = new RectOffset();
            style.contentOffset = Vector2.zero;
            style.alignment = area ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            style.wordWrap = area;
            // Dynamic faces can put a descender below the height reported by IMGUI. Keep
            // input text from losing its bottom pixel while the outer Slab still owns the
            // field's exact geometry.
            style.clipping = TextClipping.Overflow;

            // Let GUI.color carry the scheme ramp (and caller placeholder tints). A skin
            // with a dark-entry text color must not turn the same control black on a dark well.
            style.normal.textColor = Color.white;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.focused.textColor = Color.white;
            style.onNormal.textColor = Color.white;
            style.onHover.textColor = Color.white;
            style.onActive.textColor = Color.white;
            style.onFocused.textColor = Color.white;
            styles[of] = style;
            return style;
        }
    }
}
