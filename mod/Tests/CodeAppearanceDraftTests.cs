using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class CodeAppearanceDraftTests
    {
        public static void AppearanceDraft()
        {
            var settings = new ModSettings { codeBatTheme = "old", codeLineNumbers = true };
            var draft = CodeAppearanceDraft.For(settings);
            draft.SelectTheme("bat", "new");
            draft.LineNumbers = false;
            AssertEx.True(draft.Dirty, "theme and numbers are unsaved");
            AssertEx.Equal("new", draft.Theme("bat"), "preview uses draft theme");
            AssertEx.Equal("old", CodeHighlight.Theme(settings, "bat"), "readers retain applied theme");
            AssertEx.True(settings.codeLineNumbers, "readers retain applied numbers");
            AssertEx.True(ReferenceEquals(draft, CodeAppearanceDraft.For(settings)), "page reopening retains draft");
            draft.Discard();
            AssertEx.Equal("old", draft.Theme("bat"), "discard restores theme");
            AssertEx.True(draft.LineNumbers, "discard restores numbers");
            AssertEx.False(draft.Dirty, "discard is clean");
            draft.LineNumbers = false;
            int writes = 0;
            var save = draft.CaptureSave(value => { writes++; AssertEx.False(value.codeLineNumbers, "persist submitted numbers"); });
            AssertEx.Equal(0, writes, "capturing does not persist");
            AssertEx.True(settings.codeLineNumbers, "capturing does not apply");
            save();
            AssertEx.Equal(1, writes, "save persists once");
            AssertEx.False(settings.codeLineNumbers, "save applies numbers");
            AssertEx.False(draft.Dirty, "saved draft is clean");
        }
        public static void AppearancePendingSave()
        {
            var settings = new ModSettings { codeBatTheme = "old" };
            var draft = CodeAppearanceDraft.For(settings);
            draft.SelectTheme("bat", "submitted");
            draft.LineNumbers = false;
            var save = draft.CaptureSave(_ => { });
            draft.SelectTheme("bat", "next");
            draft.LineNumbers = true;
            save();
            AssertEx.Equal("submitted", settings.codeBatTheme, "apply submitted theme");
            AssertEx.False(settings.codeLineNumbers, "apply submitted numbers");
            AssertEx.Equal("next", draft.Theme("bat"), "keep newer theme");
            AssertEx.True(draft.LineNumbers, "keep newer numbers");
            AssertEx.True(draft.Dirty, "newer edits remain unsaved");
        }

        public static void AppearanceSaveFailure()
        {
            var settings = new ModSettings { codeBatTheme = "old", codeLineNumbers = true };
            var draft = CodeAppearanceDraft.For(settings);
            draft.SelectTheme("bat", "new");
            draft.LineNumbers = false;
            var save = draft.CaptureSave(_ => { throw new InvalidOperationException("write failed"); });
            bool failed = false;
            try { save(); }
            catch (InvalidOperationException) { failed = true; }
            AssertEx.True(failed, "persistence failure is reported");
            AssertEx.Equal("old", settings.codeBatTheme, "failed write restores applied theme");
            AssertEx.True(settings.codeLineNumbers, "failed write restores applied numbers");
            AssertEx.True(draft.Dirty, "failed draft remains available for retry");
        }

    }
}
