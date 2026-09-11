# UI color review

Paths below are relative to `mod/Source/SlopWorld/`.

## High priority

1. **Sidebar tabs overlap at minimum width.** At 150px, Library and the filter occupy
   almost the same rectangle. Make the strip responsive or raise the minimum width.
   `Patches/AgentSidebar.cs:20`, `Patches/AgentSidebar/AgentSidebar.Chrome.cs:53`.
2. **Search filters are too narrow.** At the default 210px sidebar width, each filter gets
   only 42.5px including its checkbox. Split filters across rows or adapt to available width.
   `UI/SearchView.cs:138`.
3. **Light-scheme status text lacks contrast.** Examples include GNOME Light waiting text
   at about 1.98:1 and Tango Light working text at 2.39:1 against their window backgrounds.
   Separate readable status-text colors from marker colors.
   `UI/UIScheme.cs:318`, `UI/TasksView.cs:148`, `UI/TopBar.cs:233`.

## Medium priority

4. **Icon hover colors bypass the scheme.** Top-bar doors, row actions, and the sidebar
   add icon turn white. Use scheme-derived `Lead`, as shared icon buttons do.
   `UI/TopBar.cs:156`, `UI/RowActions.cs:47`,
   `Patches/AgentSidebar/AgentSidebar.Chrome.cs:41`.
5. **Hidden clock and usage leave unused space.** Status remains limited to half the bar
   when both are hidden. Extend it to the remaining space before the doors.
   `UI/TopBar.cs:66`.
6. **Full-size buttons have fixed height.** The 30px `BtnH` risks clipping larger custom
   fonts. Derive height from `LineH` with a 30px minimum; verify with larger fonts.
   `UI/UiTheme.cs:137`, `UI/Settings/AppearancePage.cs:178`.
7. **Closing rules use logical-pixel offsets.** `yMax - 1f` can leave seams at larger UI
   scales. Use `Slab.LineW` for rules intended to close a frame; verify at fractional scales.
   `UI/UiTable.cs:72`, `Patches/AgentSidebar/AgentSidebar.Chrome.cs:224`.

## Low priority

8. **Slider alignment depends on label width.** Labels exceeding the 120px minimum stagger
   tracks; fixed 46px readouts risk clipping larger fonts. Share label-column widths within
   groups and measure readouts. `UI/UiControls.cs:354`.
9. **Picker close buttons differ from window chrome.** Pickers use a 44px-wide text X with
    dynamic height; windows use a 22px square icon. Reuse shared close chrome.
    `UI/UiPickerWindow.cs:86`, `UI/UiWindow.cs:102`.
10. **Jukebox headers use the selection color.** Use `RowBg` instead of `RowOn` to match
    shared table headers. `UI/JukeboxHistoryView.cs:111`, `UI/UiTable.cs:60`.

## Excluded

- Different scrollbar-gutter policies are reasonable for forms and lists.
- Logical `1f` content padding is not inherently a defect; the rule-offset finding is narrower.
