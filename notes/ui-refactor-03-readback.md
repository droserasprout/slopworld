# Step 3: GPU texture readback

Status: complete. Dependency: none. [Shared validation](mod-refactoring-plan.md).

DeadCursor and MenuBackgroundBake contain equivalent ReadBack methods without exception-safe
temporary resource cleanup.

## Implementation

1. Add an internal TextureReadback utility under `UI/Utilities/` returning Color[].
2. Preserve dimensions, render-target and Texture2D formats, mipmap setting, and the
   Blit/ReadPixels/Apply/GetPixels sequence.
3. Track resources as acquired. Restore RenderTexture.active before releasing the temporary
   target, and destroy the copy texture on success or failure. Use nested finally blocks
   where necessary so one cleanup cannot skip the other.
4. Replace the methods in `UI/Chrome/DeadCursor.cs` and
   `UI/MenuBackground/MenuBackgroundBake.cs` with utility calls.

## Validation and completion

Run shared C# checks. Review exception boundaries, including allocation and GetPixels, for
restoration and cleanup of all acquired resources. No GPU mocks or image comparisons are
needed for this small extraction. Runtime GPU behavior remains unverified.

Done when one readback implementation serves both callers and all temporary resources have
explicit cleanup. Land as one independent change.
