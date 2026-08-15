# Refactor: split Worksite.cs

Owns: `mod/Source/SlopWorld/Sim/Worksite.cs` (new sibling files under `Sim/` are
fine). See [mod-worksite](mod-worksite.md).

875 lines, flagged at 108 members — but the count is inflated. The file holds the
`Worksite` MapComponent, several inner types (`Errand`, `Run`, the frame-tracking
state) **and three Harmony patch classes** (`Patch_ErrandWork`, `Patch_ErrandDone`,
`Patch_HideFloorFrames`). Several responsibilities, one file.

Steps (mechanical, low risk):

- Move the three `Patch_*` classes to `Sim/WorksitePatches.cs`.
- Move the errand table types (`Errand`, `Run`) and the static `_errands` list to
  `Sim/WorksiteErrands.cs`, or a `partial class Worksite` in `Worksite.Errands.cs`.
- Leave the reconcile/tick logic in `Worksite.cs`.

Prefer `partial class Worksite` for extracted state so no member accessors change.

Done when: `Worksite.cs` is the MapComponent only; the patch classes and the
errand table each have their own file.
