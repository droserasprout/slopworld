# Packaging and releases

Build distributable packages or publish a versioned GitHub release. Start with
[Build from source](build.md) for the toolchain and development checks.

## Versioned GitHub releases

### Requirements

Publish from a clean checkout on an Arch Linux x86_64 host with RimWorld installed,
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

Both recipes write these assets into `dist/<version>/`:

- `slopworld-<version>-x86_64-linux.tar.gz`
- `slopworld-<version>-mod.zip`
- `slopworld-<version>-x86_64.pkg.tar.zst`
- `slopworld-<version>-amd64.deb`
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
build version in `VERSION`. Exact numeric `MAJOR.MINOR.PATCH` tags at HEAD, optionally
prefixed by `v`, set the release version. Untagged checkouts append the UTC build
date and short hash to the package version. Without Git data, builds use the
package version alone. `VERSION` overrides it for both components.

Archive and native package filenames include the resolved version; package metadata carries the same version. `SHA256SUMS`
covers both archives and both packages.

### Publish

Before publishing, run the [checks](build.md#checks) and push the commit to GitHub. Authenticate
the GitHub CLI with `gh auth login`; its selected repository is the destination.
Use `GH_REPO=owner/repo` to select another repository, or `GH` to override the CLI
command. Publishing creates a release named `v<version>` and creates its remote tag
at the built commit if it does not exist. An existing tag must point to that commit;
existing releases are never replaced. Local tags stay unchanged.

Publication requires a numeric release version. For 0.0.1, tag the release commit `v0.0.1` before building, or use
`just VERSION=0.0.1 release-publish`. Push the commit before publishing. If publication
fails after creating the tag, rerun from the same commit. If a release was created
with incomplete assets, inspect and repair that release explicitly; the publisher
refuses to overwrite it.

Sidecar CI publishes commit SHA and resolved-version image tags, including on
numeric release-tag pushes. The image label and embedded daemon version use that
same version. Untagged builds receive dated snapshot versions.

Use `just test-release` for game-free packaging and publication tests.

## Debian packages

Build on the Debian or Ubuntu release where the package will run, with the
[build prerequisites](build.md#prerequisites), ALSA development headers (`libasound2-dev`),
and `dpkg-dev` installed. RimWorld's native Linux assemblies are required.

```sh
RIMWORLD=/path/to/RimWorld/game just pkg-debian
```

The recipe builds Release binaries and the mod with one version, checks staged
licenses, and writes `slopworld_<version>-1_<architecture>.deb` to `dist/debian/`.
Use `just DEB_DIR=/path/to/output pkg-debian` to change the output directory.
It supports native `amd64` and `arm64` builds; game availability and runtime
validation are separate requirements. `VERSION` overrides the shared build version.
The recipe packages the working tree; commit changes first for a reproducible source revision.

The package includes all three binaries, the user service, desktop entry, icon,
mod, and license notices. Shared-library requirements come from
[dpkg-shlibdeps](https://manpages.debian.org/bookworm/dpkg-dev/dpkg-shlibdeps.1.en.html)
on the build host; build on the oldest Debian/Ubuntu release you intend to support.
Game assemblies and untracked mod assets are excluded. Package installation and
user setup are covered in [Install](../getting-started/install.md#debian-and-ubuntu).
`just test-release` covers Debian staging and failure handling without the game.

