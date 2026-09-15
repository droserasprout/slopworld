# Prevent Git ignore classification from blocking file browsing

## Problem and ownership

`slopd/src/api/handlers_files.rs` writes every filename to `git check-ignore --stdin -z`
before reading stdout. With enough ignored filenames, Git blocks writing results while
the daemon blocks writing input. There is no timeout, so the browse request can hang.
Classification runs even when ignored entries are shown, because the UI needs their tint.

Review reproduction: an isolated repository with 500 ignored filenames of 240 characters
blocked the sequential write; draining stdout unblocked it and returned all 500 entries.

The handler owns classification and fallback behavior. `slopd/src/process.rs` owns bounded
child capture, timeout and cleanup; `git.rs` owns the restricted inspection command.
Existing browse/classification tests live in `api/handlers.rs`.

## Implementation

1. Write stdin, drain output and wait for exit concurrently. Close stdin after the final
   NUL-delimited filename so Git receives EOF. Preserve safe handling of filename bytes.
2. Extend the shared process helper to support supplied stdin if appropriate; its current
   `run_bounded` explicitly uses null stdin. Keep one timeout around the whole exchange,
   including input writes, and bound retained output while continuing to drain the pipe.
3. Preserve the restricted Git command. Kill and reap on timeout; ensure I/O errors and
   request cancellation release the child and any input writer without detached work.
4. Treat classification failure as unavailable metadata: return the original listing
   without hiding entries based on partial output. Accept Git's normal no-match exit code;
   reject failed or truncated classification before applying results.

## Acceptance

- A real temporary repository with 500 long ignored filenames completes within a test
  deadline and classifies every entry, both when showing and hiding ignored entries.
- A controlled child that fills stdout before consuming all stdin cannot deadlock the
  process helper. A stalled child times out and is reaped.
- No-match results, missing Git/non-repositories, early child exit and output overflow
  return promptly with the intended listing fallback. Cancellation releases resources.
- Keep NUL-delimited filename handling and existing browse limits intact. Use temporary
  repositories and controlled children; run `make test-daemon` and `make lint-daemon`.

Update a focused note only if process ownership changes, then delete this plan when complete.
