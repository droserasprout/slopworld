using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // One owner for native text-entry identity and all edits that arrive after the IMGUI
    // pass. UiText keeps the public field shapes and frame policy; this controller owns the
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

        public static string Draw(Rect r, string text, bool area, bool focused, string name,
                                  bool readOnly = false)
        {
            string source = text ?? "";
            var wasColor = GUI.color;
            bool over = Mouse.IsOver(r);
            // Most callers leave GUI.color white. Give ordinary entries the same quiet text
            // ramp as menu and sidebar labels, while preserving deliberate placeholder/error
            // tints supplied by a caller.
            if (wasColor == Color.white)
                GUI.color = focused || over ? UiText.Lead : UiText.Name;

            var e = Event.current;
            bool eventOver = e != null && r.Contains(e.mousePosition);
            int button = -1;
            bool mouseDown = eventOver && MouseDown(e, out button);
            var style = Bare(area ? Verse.Text.CurTextAreaStyle
                                  : Verse.Text.CurTextFieldStyle, area);
            // Bind selection and read-only restoration to the exact control we draw rather
            // than resolving it through the global focus name. Touching another row's
            // editor would clamp its cursor to our text length.
            int controlId = GUIUtility.GetControlID(name.GetHashCode(), FocusType.Keyboard, r);
            var fieldEditor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), controlId);
            var focusedEditor = GUIUtility.keyboardControl == controlId ? fieldEditor : null;
            var prepared = TextFieldSelection.Prepare(r, name, source, style,
                focusedEditor, FieldLifetimeScope.Current);
            bool replay = (prepared == TextFieldSelection.PrepareResult.None ||
                prepared == TextFieldSelection.PrepareResult.TrackedMouseDown) &&
                (mouseDown && (e.type == EventType.Used || e.type == EventType.ContextClick));
            if (!replay && prepared == TextFieldSelection.PrepareResult.None &&
                TextFieldSelection.ShouldReplay(name, e, FieldLifetimeScope.Current))
                replay = true;
            var oldType = replay ? e.type : EventType.Ignore;
            EventType replayType = UiEvent.RawType(e);
            var beforeEditor = mouseDown && button == 1 ? focusedEditor : null;
            bool keepSelection = beforeEditor != null && beforeEditor.IsOverSelection(e.mousePosition);
            int beforeCursor = keepSelection ? beforeEditor.cursorIndex : 0;
            int beforeSelect = keepSelection ? beforeEditor.selectIndex : 0;

            // WindowStack may have consumed the event before this window's contents run. Give
            // the native editor its mouse-down once, then leave the event Used as before. This
            // is what lets a click focus a field even when it sits below an absorbing window.
            if (replay) e.type = replayType == EventType.ContextClick
                ? EventType.MouseDown : replayType;

            try
            {
                var content = new GUIContent(source);
                DrawNativeTextField(r, controlId, content, area, -1, style);
                var result = content.text;

                var editor = GUIUtility.keyboardControl == controlId ? fieldEditor : null;
                if (keepSelection && editor != null)
                {
                    editor.cursorIndex = beforeCursor;
                    editor.selectIndex = beforeSelect;
                }
                ApplyPendingEdits(name, editor, FieldLifetimeScope.Current);
                if (readOnly && editor != null && editor.text != source)
                {
                    int cursor = editor.cursorIndex;
                    int select = editor.selectIndex;
                    editor.text = source;
                    editor.cursorIndex = Mathf.Clamp(cursor, 0, source.Length);
                    editor.selectIndex = Mathf.Clamp(select, 0, source.Length);
                }
                if (editor != null) result = readOnly ? source : editor.text;
                if (!readOnly) FieldFocusScope.Register(name, controlId, r);

                if (prepared == TextFieldSelection.PrepareResult.MultiClick)
                    TextFieldSelection.FinishMultiClick(name, editor,
                        FieldLifetimeScope.Current);

                if (prepared == TextFieldSelection.PrepareResult.None ||
                    prepared == TextFieldSelection.PrepareResult.SelectionDrag)
                    TextFieldSelection.Handle(r, name, source, style, editor,
                        FieldLifetimeScope.Current);

                if (MouseUp(e)) CopyPrimarySelection(editor);

                if (mouseDown && button == 2 && !readOnly)
                {
                    RequestPaste(name, area, primary: true, lifetime: FieldLifetimeScope.Current);
                    e.Use();
                }
                else if (mouseDown && button == 1)
                {
                    OpenContextMenu(name, area, editor, FieldLifetimeScope.Current, readOnly);
                    e.Use();
                }

                return result;
            }
            finally
            {
                // A ContextClick that the native field did not consume still belongs to us.
                // Restore only an event that is genuinely still live; a native Use() must stay
                // Used so lower layers do not interpret the same click a second time.
                if (replay && e.type != EventType.Used) e.type = oldType;
                GUI.color = wasColor;
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
            // the original left-button event. Do not lose PRIMARY publication in that case;
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
                    // The daemon is unavailable in sidecar mode; Unity's local buffer is the
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

            // Let GUI.color carry the scheme ramp (and caller placeholder tints); a skin
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
