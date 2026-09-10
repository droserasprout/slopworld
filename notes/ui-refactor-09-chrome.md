# Step 9: Shared chrome cleanup

Status: complete; preview geometry already shared. Dependencies: steps 2, 4 and 7;
finish after feature changes. [Shared validation](mod-refactoring-plan.md).

The UiWidgets inheritance chain obscures ownership, UiText owns native editor machinery,
and UiScrollBody exposes similar APIs with different scrollbar reservation.

## Implementation slices

1. Factor shared RowLabel geometry/state setup in `UI/Chrome/UiText.cs`, preserving italic
   rendering and WidgetState restoration.
2. Extract an internal text-entry controller for native invocation, exact control-ID lookup,
   pending edits, clipboard requests and focus restoration. Keep Field, Area, ReadOnlyField,
   field names and FieldLifetime semantics unchanged.
3. Replace View/ConditionalView with one geometry result and explicit Always/WhenNeeded
   scrollbar reservation. Migrate each caller using its current policy; preserve wrapping
   width and content height.
4. Move controls into focused owning types; replace the empty UiWidgets inheritance chain
   with explicit compatibility forwarding. Inspect intermediate button types too.
   Migrate callers by feature and remove forwarding only when unused.
5. Appearance and Terminal already share SettingsPreviewLayout. Compare remaining scroll,
   measurement and preview hosting after step 2. Extract a host only if duplicate lifecycle
   policy remains; retain existing geometry and specialized preview drawing.

## Validation and completion

Land each slice separately with shared C# checks. Extend pure scroll geometry tests for both
reservation policies below, at and above viewport height. Reuse focus tests for identity and
stale deferred edits. Native Unity input remains a source-review limitation.
Mechanical forwarding and RowLabel deduplication do not need new tests alone.

Done when chrome ownership is explicit, scrollbar policy is named at call sites and native
text entry has one controller. Update mod-ui-chrome.md and ui-focus.md. New style architecture
and new input behavior are outside scope.

Implemented the five slices: shared RowLabel geometry, `TextEntryController`, named scroll
geometry policies, and explicit `UiWidgets` forwards to focused chrome owners. Appearance and
Terminal were compared after the extraction; `SettingsPreviewLayout` already owns their common
arrangement, so their specialized preview drawing and independent scroll lifetimes remain in
the feature pages. `make test-mod`, `make lint-mod` and `make lint-prose` pass; native Unity
input remains source-review only.
