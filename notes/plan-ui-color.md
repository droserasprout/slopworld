# UI color review

Remeasure layout and contrast before applying fixes. Paths below are relative to `mod/Source/SlopWorld/`.

## High priority

1. **Sidebar tabs overlap at minimum width.** At 150px, Library and the filter occupy
   almost the same rectangle. Make the strip responsive or raise the minimum width.
   `Patches/AgentSidebar/AgentSidebar.cs`, `Patches/AgentSidebar/AgentSidebar.Chrome.cs`.
2. **Search filters are too narrow.** At the default 210px sidebar width, each filter gets
   one quarter of the available filter width including its checkbox. Split filters across rows or adapt to available width.
   `UI/Views/Search/SearchView.cs`.
3. **Recheck light-scheme status contrast.** Measure GNOME Light waiting text and
   Tango Light working text against their backgrounds. Separate readable status-text
   roles from marker colors where needed; preserve imported palette tokens and use
   explicit mappings from [palette references](reference-original-palettes.md).
   `UI/Chrome/UIScheme.cs`, `UI/Views/Tasks/TasksView.cs`, `UI/Chrome/TopBar.cs`.

## Medium priority

4. **Icon hover colors bypass the scheme.** Top-bar doors, row actions, and the sidebar
   add icon turn white. Use scheme-derived `Lead`, as shared icon buttons do.
   `UI/Chrome/TopBar.cs`, `UI/Views/Shared/RowActions.cs`,
   `Patches/AgentSidebar/AgentSidebar.Chrome.cs`.
5. **Hidden clock and usage leave unused space.** Status remains limited to half the bar
   when both are hidden. Extend it to the remaining space before the doors.
   `UI/Chrome/TopBar.cs`.
6. **Closing rules use logical-pixel offsets.** `yMax - 1f` can leave seams at larger UI
   scales. Use `Slab.LineW` for rules intended to close a frame; verify at fractional scales.
   `UI/Chrome/UiTable.cs`, `Patches/AgentSidebar/AgentSidebar.Chrome.cs`.

## Low priority

7. **Slider alignment depends on label width.** Labels exceeding the 120px minimum stagger
   tracks; fixed 46px readouts risk clipping larger fonts. Share label-column widths within
   groups and measure readouts. `UI/Chrome/UiControls.cs`.
8. **Picker close buttons differ from window chrome.** Pickers use a 44px-wide text X with
    dynamic height; windows use a 22px square icon. Reuse shared close chrome.
    `UI/Chrome/UiPickerWindow.cs`, `UI/Chrome/UiWindow.cs`.
9. **Jukebox headers use the selection color.** Use `RowBg` instead of `RowOn` to match
    shared table headers. `UI/Jukebox/JukeboxHistoryView.cs`, `UI/Chrome/UiTable.cs`.

## Excluded

- Different scrollbar-gutter policies are reasonable for forms and lists.
- Logical `1f` content padding is not inherently a defect; the rule-offset finding is narrower.
