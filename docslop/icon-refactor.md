# Icon Refactoring Plan

## Goal

Consolidate all icon code into one place, replace ugly procedural icons with emoji-derived alpha masks (same pipeline as `tools/emoji.py` → `TrophyIcon`), delete the nine individual `*Icon.cs` files.

## Current state

Nine separate `[StaticConstructorOnStartup]` classes, each with its own `new Texture2D(32,32)` boilerplate, pixel math, `SetPixels`/`Apply`/`hideFlags`:

| File | Lines | Pattern | Used at |
|---|---|---|---|
| `TerminalIcon.cs` | ~70 | signed-distance strokes | gizmo, session list, options row |
| `GearIcon.cs` | ~70 | polar cos-teeth | options row (gear) |
| `ShieldIcon.cs` | ~70 | 4× supersampled triangle | options row (sandbox) |
| `PowerIcon.cs` | ~70 | 4× supersampled triangle/square | gizmo (play/stop) |
| `MarkIcon.cs` | ~100 | 4× supersampled capsules + disc | switches, toggles, dots |
| `TabIcons.cs` | ~320 | 4× supersampled predicates (12 shapes) | sidebar tabs, top bar, row actions, usage page |
| `TypeIcon.cs` | ~100 | signed-distance strokes + disc | options row (appearance) |
| `Slab.cs` | ~280 | supersampled corner arcs | layout primitive — NOT an icon, keep as-is |
| `DeadCursor.cs` | ~230 | ReadBack + spin animation | hardware cursor — NOT a UI icon, keep as-is |

Two proven prebake pipelines already exist, both producing alpha-mask PNGs loaded via `ContentFinder<Texture2D>.Get()`:

- `tools/fileicons.py` — Material Icon Theme SVGs → 32px RGBA with 4× supersample → `mod/Textures/SlopWorld/FileIcons/<name>.png`
- `tools/emoji.py` — Noto Color Emoji via PangoCairo → alpha mask → `mod/Textures/SlopWorld/FileIcons/<name>.png`

## Phase 1: New `Icons` registry

Create `UI/Icons.cs` — one class to rule them all:

```csharp
[StaticConstructorOnStartup]
public static class Icons
{
    const string Dir = "SlopWorld/Icons/";
    static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D Terminal   => Get("terminal");
    public static Texture2D Gear       => Get("gear");
    public static Texture2D Shield     => Get("shield");
    public static Texture2D Play       => Get("play");
    public static Texture2D Stop       => Get("stop");
    public static Texture2D Check      => Get("check");
    public static Texture2D Cross      => Get("cross");
    public static Texture2D Dot        => Get("dot");
    public static Texture2D Robot      => Get("robot");
    public static Texture2D Folder     => Get("folder");
    public static Texture2D Branch     => Get("git-branch");
    public static Texture2D Lightning  => Get("lightning");
    public static Texture2D Refresh    => Get("refresh");
    public static Texture2D Eye        => Get("eye");
    public static Texture2D Bell       => Get("bell");
    public static Texture2D Hamburger  => Get("hamburger");
    public static Texture2D Edit       => Get("edit");
    public static Texture2D Diff       => Get("diff");
    public static Texture2D Type       => Get("type");
    public static Texture2D Trophy     => Get("trophy");

    static Texture2D Get(string name) { … lazy ContentFinder load + cache + hideFlags }
}
```

Same lazy-init/cache/hideFlags pattern as `TrophyIcon` and `FileIcons.Tex`. All lookups via `ContentFinder<Texture2D>.Get(Dir + name, false)`.

## Phase 2: Bake action icons from emoji

Create `tools/action_icons.py` — a single bake script. Renders each icon from Noto Color Emoji at 32px via PangoCairo, writes mono alpha-mask PNG to `mod/Textures/SlopWorld/Icons/<name>.png`.

Reuse `emoji.py`'s rendering code (import or inline, it's ~60 lines of real work).

### Emoji mapping

| Icon name | Emoji | Replacees |
|---|---|---|
| `terminal` | 💻 | `TerminalIcon` |
| `gear` | ⚙️ | `GearIcon`, `TabIcons.ConfigTex` (used in 2 places) |
| `shield` | 🛡️ | `ShieldIcon` |
| `play` | ▶️ | `PowerIcon.StartTex` |
| `stop` | ⏹️ | `PowerIcon.StopTex` |
| `check` | ✅ or ✓ | `MarkIcon.CheckTex` |
| `cross` | ❌ or ✗ | `MarkIcon.CrossTex`, `TabIcons.AutoTex` |
| `dot` | ● | `MarkIcon.DotTex` (or keep procedural — 3 lines) |
| `robot` | 🤖 | `TabIcons.AgentsTex` |
| `folder` | 📁 | `TabIcons.FilesTex` (or copy `FileIcons/folder-base`) |
| `lightning` | ⚡ | `TabIcons.ShortcutsTex` |
| `refresh` | 🔄 | `TabIcons.RefreshTex` |
| `eye` | 👁️ | `TabIcons.HiddenTex`/`ViewTex` (alias) |
| `bell` | 🔔 | `TabIcons.BellTex` |
| `edit` | ✏️ | `TabIcons.EditTex` |
| `type` | 🔤 | `TypeIcon` (`Aa`) |
| `hamburger` | (none) | `TabIcons.HamburgerTex` — use Material Icon Theme `menu` SVG via fileicons pipeline |
| `git-branch` | (none) | `TabIcons.GitTex` — use Material Icon Theme `git-branch` SVG |
| `diff` | (none) | `TabIcons.DiffTex` — already exists as `FileIcons/diff.png`, just copy/move |
| `trophy` | (none) | `TrophyIcon` — already exists as `FileIcons/trophy.png`, alias in Icons or just register |

### Notes

- `TabIcons.HiddenTex` == `ViewTex` (alias), one icon serves both
- `TabIcons.ConfigTex` == same gear icon used for `GearIcon`
- `TabIcons.AutoTex` == cross/x mark
- `hamburger` and `git-branch` have no good emoji equivalents — use Material Icon Theme SVGs via `fileicons.py` renderer instead, output to same `Icons/` dir
- `diff` and `trophy` already exist as prebaked PNGs in `FileIcons/` — copy or move to `Icons/` dir

## Phase 3: Update call sites

| Old | New | File(s) |
|---|---|---|
| `TerminalIcon.Tex` | `Icons.Terminal` | `PawnGizmoPatch.cs:33`, `SessionsView.cs:106`, `SlopOptions.cs:436` |
| `GearIcon.Tex` | `Icons.Gear` | `SlopOptions.cs:239` |
| `ShieldIcon.Tex` | `Icons.Shield` | `SlopOptions.cs:533` |
| `PowerIcon.StartTex` | `Icons.Play` | `PawnGizmoPatch.cs:60` |
| `PowerIcon.StopTex` | `Icons.Stop` | `PawnGizmoPatch.cs:44` |
| `MarkIcon.CheckTex` | `Icons.Check` | `SlopWidgets.cs:316`, `SlopWidgets.cs:431` |
| `MarkIcon.CrossTex` | `Icons.Cross` | `SlopWidgets.cs:431` |
| `MarkIcon.DotTex` | `Icons.Dot` | `SlopWidgets.cs:474` |
| `TabIcons.AgentsTex` | `Icons.Robot` | `AgentSidebar.cs:648` |
| `TabIcons.FilesTex` | `Icons.Folder` | `AgentSidebar.cs:651` |
| `TabIcons.GitTex` | `Icons.Branch` | `AgentSidebar.cs:654` |
| `TabIcons.ShortcutsTex` | `Icons.Lightning` | `AgentSidebar.cs:658`, `SlopOptions.cs:626` |
| `TabIcons.HiddenTex` | `Icons.Eye` | `AgentSidebar.cs:671` |
| `TabIcons.RefreshTex` | `Icons.Refresh` | `AgentSidebar.cs:689` |
| `TabIcons.BellTex` | `Icons.Bell` | `AgentSidebar.cs:810` |
| `TabIcons.HamburgerTex` | `Icons.Hamburger` | `TopBar.cs:111` |
| `TabIcons.ConfigTex` | `Icons.Gear` | `TopBar.cs:114` |
| `TabIcons.AutoTex` | `Icons.Cross` | `UsagePage.cs:324` |
| `TabIcons.ViewTex` | `Icons.Eye` | `RowActions.cs:64,143` |
| `TabIcons.EditTex` | `Icons.Edit` | `RowActions.cs:65,144` |
| `TabIcons.DiffTex` | `Icons.Diff` | `RowActions.cs:66,145` |
| `TypeIcon.Tex` | `Icons.Type` | `SlopOptions.cs:309` |
| `TrophyIcon.Tex` | `Icons.Trophy` | `SlopOptions.cs:581` |

## Phase 4: Remove old files

Delete after updating all references:
- `UI/TerminalIcon.cs`
- `UI/GearIcon.cs`
- `UI/ShieldIcon.cs`
- `UI/PowerIcon.cs`
- `UI/MarkIcon.cs`
- `UI/TabIcons.cs`
- `UI/TypeIcon.cs`
- `UI/TrophyIcon.cs`

**Keep as-is:**
- `UI/Slab.cs` — rounded-rect layout primitive, not an icon
- `UI/DeadCursor.cs` — hardware cursor with animation
- `Sim/RobotFace.cs` — per-pawn render node textures (exempted)
- `UI/FileIcons.cs` — extension-based file-type lookups, separate concern
- `UI/MenuBackground.cs` — full-frame background effects

## What stays procedural

1. **`Slab` corner textures** — must match screen-pixel grid at current UI scale, rebuilt on scale change. Cannot prebake.
2. **`Dot`** (filled disc) — 3 lines of pixel math; keep in `Icons.cs` as inline fallback, or bake from `●` emoji.

## Why this works

- **Visual quality**: Noto Color Emoji at 32px via PangoCairo with 4× supersample (the `--size 128` + LANCZOS downscale `emoji.py` already does) is dramatically better than hand-written predicate math.
- **Same contract**: Alpha masks tinted at runtime via `GUI.color` — identical to procedural icons. Zero rendering pipeline changes.
- **One bake script**: `tools/action_icons.py` is `emoji.py` with a hardcoded name→emoji map. All output to one directory, committed to repo.
- **One loader**: `Icons.cs` is `FileIcons.cs` without extension-lookup complexity. Same `ContentFinder` path, same lazy cache, same `hideFlags`.
- **No mod size increase**: Each icon PNG is ~200-400 bytes after optimization. Twenty icons ≈ under 10KB.