# Handoff: sidebar face portraits

## Goal

Replace the full-body colonist bar portraits in the sidebar layout with a
close-up face shot. The blue background (BGTex, mood bars, mood gradient) is
suppressed, and a stopped agent's portrait is greyed out instead of showing a
cross icon.

## Approach taken

A Harmony prefix on `ColonistBarColonistDrawer.DrawColonist` that returns
`false` when the sidebar is drawing (`ColonistBarStrip.Drawing`), replacing the
vanilla drawing entirely. The map-layer bar (strip layout) is unaffected.

**File:** `mod/Source/SlopWorld/Patches/ColonistBarDrawPatch.cs`

The prefix draws:
1. Highlight border (white box, same as vanilla)
2. Selection brackets (via `DrawSelectionOverlayOnGUI` reflection)
3. Face-only portrait via `PortraitsCache.Get` with custom camera params
4. Vanilla icons via `DrawIcons` reflection (our `Patch_ColonistBarStateIcon`
   postfix rides along)
5. Dead overlay

## Current state: broken

The user tested the result three times and each time said it was still wrong.
The specific complaints in order:

1. **Heads half size, bodies visible, heads missing instead of greyed out**
   - Fixed: camera offset `y=0.5→1.65`, zoom `2.3→12.0`, added
     `overrideHealthState=Mobile` for downed pawns

2. **Heads still cropped, still off. "Remove neck/shoulders, leave only face"**
   - Fixed: zoom `12.0→6.0`, head rect re-centred in cell, grey tint
     `0.32→0.50`

3. **Face too zoomed in (eyes cropped), icon space smaller than original**
   - Fixed: zoom `12.0→7.0`, head rect `cellSize→cellSize*1.5`, positioned at
     row top

After the third fix the user said "Still completely broken" and asked for this
handoff.

## What's wrong

The root cause was never identified. Likely candidates:

### Camera calibration is guesswork

The `PortraitsCache.Get` camera offset and zoom values are pure guesswork — the
codebase has no test harness and the values were adjusted by trial and error
through the user's feedback loop, which was too slow to converge. The correct
values depend on:
- The pawn's height in game units (roughly 1.8 for a human)
- Where the head/face sits in those units (roughly y=1.3–1.7)
- How `cameraOffset` maps to the render (is it the camera position or the
  target? Is z forward distance or lateral?)
- How `cameraZoom` scales (1.28205 shows the full body in a 75px texture)

These should be read from the game's own portrait rendering code rather than
guessed. Look at how `Dialog_NamePawn` or the character card position their
cameras — they use `Vector3.zero` offset and `zoom=1.0` for a full-body shot
at 128×128, which is a different framing than the colonist bar's 46×75.

### The head rect position is fragile

The head rect is positioned relative to the cell rect (`rect` parameter of
`DrawColonist`). The vanilla full-body portrait extends 27*scale above the
cell (`PawnTextureSize.y - BaseSize.y = 75 - 48 = 27`). The face is at the
top of that portrait area. But the head rect needs to be centred on the face,
and the face position in the row depends on the camera offset — so the rect
position and the camera offset are coupled and must be tuned together.

### The patching approach may be wrong

The user asked "Are we taking the right approach patching existing bar instead
of creating new UI element?" The answer was "yes, patching is right" but the
continuing failures suggest otherwise. The prefix on `DrawColonist` is fragile:
- It uses reflection for 4 private methods (`GetPawnTextureRect`,
  `DrawSelectionOverlayOnGUI`, `DrawCaravanSelectionOverlayOnGUI`, `DrawIcons`)
  and 1 private field (`DeadColonistTex`)
- It must replicate the vanilla alpha calculation, selection logic, and health
  state handling
- The camera values are hardcoded constants with no way to preview them

A cleaner approach might be to **draw the sidebar portraits entirely in
`AgentSidebar.DrawBack`/`DrawFront`**, bypassing the vanilla bar machinery
for the portrait rendering while still using the bar for layout, clicks, and
icons. This would give full control over the rect and camera values without
needing to patch the bar's internal method.

## Files changed

| File | What |
|------|------|
| `mod/Source/SlopWorld/Patches/ColonistBarDrawPatch.cs` | **New.** The Harmony prefix that replaces `DrawColonist` in the sidebar. |
| `mod/Source/SlopWorld/Patches/ColonistBarStateIcon.cs` | Unchanged. The down/idle icon postfix still works because `DrawIcons` is called via reflection. |

## Key configuration values (in `Patch_SidebarPortraitDraw`)

```csharp
static readonly Vector3 HeadCameraOffset = new Vector3(0f, 1.65f, 0.3f);
const float HeadCameraZoom = 7.0f;
static readonly Vector2 HeadTextureSize = new Vector2(64f, 64f);
static readonly Color DownTint = new Color(0.50f, 0.50f, 0.50f, 1f);
// head rect: cellSize * 1.5, pinned at rowTop = rect.y - 27f * scale
```

## How to test

1. `make install-mod` (builds and copies to `Mods/SlopWorld`)
2. Launch the game via `make run`
3. Open the sidebar via the mod settings (sidebar layout)
4. Look at the colonist portraits in the sidebar column

The strip layout (non-sidebar) should be unaffected — the prefix gates on
`ColonistBarStrip.Drawing` which is only true during the sidebar's draw call.