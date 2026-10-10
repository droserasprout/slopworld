# Packaging and releases

Build distributable packages or publish a versioned GitHub release. Start with
[Build from source](build.md) for the toolchain and development checks.

## Versioned GitHub releases

### Requirements

Both release targets require a clean checkout on `main`, with a `{{#constant release_tag_format}}` release tag
at HEAD (for example, `{{#constant release_tag}}`). Use an Arch Linux x86_64 host with RimWorld installed,
`makepkg`, `fakeroot`, and `pacman`, plus Podman (default) or Docker
(`just CONTAINER=docker release-publish`). Root builds also require Bubblewrap
to run `makepkg` as an ordinary UID.

Release builds run locally because the mod needs the game's assemblies.
GitHub Actions runs game-free checks.

### Build packages

```sh
just release-package  # build Release daemon and mod; prepare local archives
just release-publish  # rebuild, then create a versioned GitHub release
```

Both recipes run the full `refresh`, `lint`, and `test` aggregates, generated-file
and license checks, text-sprite checks, and a documentation build before compiling
and packaging the release. They recheck the branch, tag, commit, and clean working
tree after every stage and after packaging. If a generator or formatter changes
tracked files, review and commit those changes before tagging the final release
commit and rerunning. The recipes never commit changes or move local tags.

Both recipes require a nonempty, dated `CHANGELOG.md` entry for the tagged version.
The human-maintained entry supplies the GitHub release description, including its
version, date, and changes. Missing, duplicate, empty, or undated entries stop the
release before validation builds. The changelog is only read.

Both recipes write these assets into `dist/{{#constant release_version}}/`:

- `slopworld-{{#constant release_version}}-x86_64-linux.tar.gz`
- `slopworld-{{#constant release_version}}-mod.zip`
- `slopworld-{{#constant release_version}}-x86_64.pkg.tar.zst`
- `slopworld-{{#constant release_version}}-amd64.deb`
- `SHA256SUMS` and `release-notes.md`

Use `RELEASE_DIR` to change the output directory.

The Debian compiler image uses Debian 13 (trixie) and rebuilds the Rust binaries against its libraries; the mod
payload is shared with the Arch build. The container also runs the Debian package
integration tests. `just release-debian-image` prepares that image separately;
`just test-release-debian` runs its real archive tests without compilation.
Container compiler caches live under `dist/debian-build/`; `CONTAINER_BUILD_ARGS`
and `CONTAINER_RUN_ARGS` accept additional build/run options.

### Package contents and versions

The daemon archive includes all three binaries, service, desktop entry, and licenses. The ZIP contains the `SlopWorld/` mod folder, runtime
DLLs, assets, and notices. Game assemblies and build sources stay out of the archives.

Archives and packaged mods record the full commit in `REVISION` and the shared
build version in `VERSION`. Exact `{{#constant release_tag_format}}` tags at HEAD set the release version,
with the `v` prefix removed from package and image versions. Both release targets reject untagged
commits and multiple distinct version tags. If `VERSION` is supplied, it must
match the tag; it cannot bypass the tag requirement.

Archive and native package filenames include the resolved version; package metadata carries the same version. `SHA256SUMS`
covers both archives and both packages.

### Publish

Before publishing, push the tagged commit to GitHub. The release target runs the
[checks](build.md#checks) itself. Authenticate
the GitHub CLI with `gh auth login`; its selected repository is the destination.
Use `GH_REPO=owner/repo` to select another repository, or `GH` to override the CLI
command. Publishing uses the selected local release tag as the release name and creates its remote tag
at the built commit if it does not exist. An existing tag must point to that commit;
existing releases are never replaced. Local tags stay unchanged.

For {{#constant release_version}}, finish the changelog entry, commit all generated updates on `main`, and
tag that commit `{{#constant release_tag}}`. Then run `just release-publish`. Push the commit before publishing. If publication
fails after creating the tag, rerun from the same commit. If a release was created
with incomplete assets, inspect and repair that release explicitly; the publisher
refuses to overwrite it.

Sidecar CI builds and publishes images only on `{{#constant release_tag_format}}` tag pushes, using
commit SHA and release-version image tags. Branch pushes do not build images, and
there is no manual trigger. The image label and embedded daemon version use the
same release version.

Use `just test-release` for game-free packaging and publication tests.

## Debian packages

Build on the Debian or Ubuntu release where the package will run, with the
[build prerequisites](build.md#prerequisites), ALSA development headers (`libasound2-dev`),
and `dpkg-dev` installed. RimWorld's native Linux assemblies are required.

```sh
RIMWORLD=/path/to/RimWorld/game just pkg-debian
```

The recipe builds Release binaries and the mod with one version, checks staged
licenses, and writes `slopworld_{{#constant release_version}}-1_<architecture>.deb` to `dist/debian/`.
Use `just DEB_DIR=/path/to/output pkg-debian` to change the output directory.
It supports native `amd64` and `arm64` builds; game availability and runtime
validation are separate requirements. `VERSION` overrides the shared build version.
The recipe packages the working tree; commit changes first for a reproducible source revision.

The package includes all three binaries, the user service, desktop entry, icon,
mod, and license notices. Shared-library requirements come from
[dpkg-shlibdeps](https://manpages.debian.org/bookworm/dpkg-dev/dpkg-shlibdeps.1.en.html)
on the build host; build on the oldest Debian/Ubuntu release you intend to support.
Game assemblies and untracked mod assets are excluded. Package installation and
user setup are covered in [Install](../installation/linux.md#debian-and-ubuntu).
`just test-release` covers Debian staging and failure handling without the game.

