# Project mounts

Mounts control which files and folders a project's agents can access. Edit them
on the project's **Mounts** tab, then restart running agents to apply changes.

The first row controls the project directory. It defaults to **Read-write**;
choose **Read-only** to prevent agents from changing its files. This row cannot
be removed.

<a id="give-agents-access-to-another-folder"></a>

## Additional paths

1. Select **Add path**.
2. Set **From** to an existing file or folder's absolute path on the daemon's
   machine, such as `/home/alex/shared-docs`.
3. Set **To** to where agents should see it. An absolute path such as `/mnt/docs`
   stays fixed; a relative path such as `docs` is inside the agent's checkout.
4. Choose **Read-only** or **Read-write**, then **Save**.

**Add project** copies another project's paths into a mount. Later changes to
that project do not update the copied paths. Select **×** to remove a mount.
Use the agent editor's **Preview** tab to check access.

## Shared cache mounts

A **Cache** mount shares build files across [worktrees](project-worktrees.md).
The tool must support sharing that cache across different source checkouts and
concurrent builds. For Rust, keep `target` directories separate per worktree.
Use **rust-cache** for shared downloads, and opt into **rust-sccache** for shared
compiler results. Install `sccache` on the host before starting sessions with
**rust-sccache**. SlopWorld creates and mounts `~/.cache/sccache` automatically. It sets `RUSTC_WRAPPER=sccache`; builds fail
if the executable is missing.

<a id="set-up-a-cache"></a>

### Cache setup

1. Select **Add path** and choose **Cache**.
2. Leave **From** blank for managed storage, or enter an absolute cache directory
   outside the project and its worktrees. SlopWorld creates it if needed.
3. Set **To** to the cache directory required by the tool.
4. Select **Save** and restart running agents.

Saving links the cache into every checkout, including **main** and future
worktrees. Host shells and agents use the same cached files.

An absolute **To** path mounts the cache only inside the sandbox, without checkout
links. Keep it outside the original checkout when using worktrees. Sidecar mode
requires an absolute destination.

<a id="if-the-destination-already-contains-files"></a>

### Cache conflicts

Existing files block creation of a cache link. Move them into the cache directory
shown in the error, remove the empty destination directory, and save again.

For a missing cache link, save the mounts again. If a link points elsewhere,
restore its original target first. Agents cannot start until required links are
correct.

<a id="find-or-stop-sharing-a-cache"></a>

### Cache storage and removal

**Settings > Storage** shows cache paths and sizes. To stop sharing a cache,
remove its mount row and save. SlopWorld removes its checkout links and keeps the
cached files. Removing projects, workers, or worktrees also keeps cache data.
