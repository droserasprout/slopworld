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

### Prepare a version

Finish the human-maintained, dated changelog entry, then run:

```sh
just release-bump-version {{#constant release_version}}
```

The command updates both Cargo manifests and their own lockfile entries, the mod's
fallback version, and Arch package defaults. It preserves dependency versions and
reads the changelog without changing it. Repeating the command for the same version
leaves matching files untouched. Review and commit the changes on `main`, then tag
the commit with the matching `vMAJOR.MINOR.PATCH` tag.

### Build packages

```sh
just release-package  # build Release daemon and mod; prepare local archives
just release-publish  # rebuild, then create a versioned GitHub release
```

Both recipes run `generate-all`, `lint`, `mod-check-build`, and `test` (including
sidecar and benchmark-helper tests), followed by generated-file and license
checks and a documentation build before compiling
and packaging the release. The Python suite includes the text-sprite checks. They recheck the branch, tag, commit, and clean working
tree after every stage and after packaging. If generation changes tracked files, review and commit those changes before tagging the final release
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
- `slopworld-{{#constant release_version}}-x86_64.rpm`
- `SHA256SUMS` and `release-notes.md`

Use `RELEASE_DIR` to change the output directory.

Debian and Fedora containers rebuild Rust against Debian 13 (trixie) and Fedora 43
libraries, then test and package the shared mod payload. Prepare images with
`just image-arch`, `just image-debian`, or `just image-fedora`; run archive tests without compiling with
`just package-test DISTRIBUTION`. The archive test accepts `arch`, `debian`, or `fedora`.
Caches live in `dist/<distribution>-build/`. Arch container tests use privileged
mode for the nested Bubblewrap namespace required by makepkg.
`CONTAINER_BUILD_ARGS` and `CONTAINER_RUN_ARGS` accept extra container options.

### Package contents and versions

The daemon archive includes all three binaries, service, desktop entry, and licenses. The ZIP contains the `SlopWorld/` mod folder, runtime
DLLs, assets, and notices. Game assemblies and build sources stay out of the archives.

Archives and packaged mods record the full commit in `REVISION` and the shared
build version in `VERSION`. Exact `{{#constant release_tag_format}}` tags at HEAD set the release version,
with the `v` prefix removed from package and image versions. Both release targets reject untagged
commits and multiple distinct version tags. If `VERSION` is supplied, it must
match the tag; it cannot bypass the tag requirement.
All product version files updated by `release-bump-version` must also match the tag.
Stale versions stop the release before any refresh, build, or publication.

Artifact filenames and package metadata carry the resolved version.
`SHA256SUMS` covers both archives and all three packages.

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

Sidecar CI builds and publishes images on `{{#constant release_tag_format}}` tag pushes, using
commit SHA and release-version image tags. Branch pushes do not build images, and
manual rebuilds use the workflow's `release_tag` input on `main`. The release tag
must already exist and be an ancestor of the build commit; it is never moved.
For example, run `gh workflow run image.yml --ref main -f release_tag={{#constant release_tag}}`.
The image label and embedded daemon version use that release version, while the
revision label and SHA image tag identify the actual build commit.

Use `just tools-test tools/release` for game-free packaging and publication tests.

`just` lists package builds and archive tests in the package group.

## Arch packages

Build on Arch with the [build prerequisites](build.md#prerequisites), `base-devel`,
and `bubblewrap` installed. RimWorld's native Linux assemblies are required.

```sh
RIMWORLD=/path/to/RimWorld/game just package-arch
```

This builds an x86_64 package in `dist/arch/` without installing it.
Use `just ARCH_DIR=/path/to/output package-arch` to choose another directory.
The package uses the shared release payload and existing PKGBUILD metadata.
`VERSION` overrides the build version; snapshot hyphens become `.r` in Arch metadata.
Use `just image-arch` followed by `just package-test arch` to test
real archives in the Arch image without compiling the game or daemon.

## Debian packages

Build on the Debian or Ubuntu release where the package will run, with the
[build prerequisites](build.md#prerequisites), ALSA development headers (`libasound2-dev`),
and `dpkg-dev` installed. RimWorld's native Linux assemblies are required.

```sh
RIMWORLD=/path/to/RimWorld/game just package-debian
```

The recipe builds Release binaries and the mod with one version, checks staged
licenses, and writes `slopworld_{{#constant release_version}}-1_<architecture>.deb` to `dist/debian/`.
Use `just DEB_DIR=/path/to/output package-debian` to change the output directory.
It supports native `amd64` and `arm64` builds; game availability and runtime
validation are separate requirements. `VERSION` overrides the shared build version.
The recipe packages the working tree; commit changes first for a reproducible source revision.

The package includes all three binaries, the user service, desktop entry, icon,
mod, and license notices. Shared-library requirements come from
[dpkg-shlibdeps](https://manpages.debian.org/bookworm/dpkg-dev/dpkg-shlibdeps.1.en.html)
on the build host; build on the oldest Debian/Ubuntu release you intend to support.
Game assemblies and untracked mod assets are excluded. Package installation and
user setup are covered in [Install](../installation/linux.md#debian-and-ubuntu).
`just tools-test tools/release` covers Debian staging and failure handling without the game.

## Fedora packages

Build on the Fedora release where the package will run, with the
[build prerequisites](build.md#prerequisites), `alsa-lib-devel`, `rpm-build`, and
`redhat-rpm-config` installed. RimWorld's native Linux assemblies are required.

```sh
RIMWORLD=/path/to/RimWorld/game just package-fedora
```

The recipe builds Release binaries and the mod, then writes
`slopworld-{{#constant release_version}}-1.<architecture>.rpm` to `dist/fedora/`.
Use `just RPM_DIR=/path/to/output package-fedora` to choose another directory.
It supports native `x86_64` and `aarch64` builds; verify game availability and runtime
compatibility separately. `VERSION` overrides the build version. RPM metadata
replaces snapshot hyphens with dots; the mod keeps the original version.
Commit changes before packaging to record a reproducible source revision.

The RPM includes all three binaries, user service, desktop entry, icon, mod,
and license notices. RPM discovers native library dependencies and excludes the
mod's private Unity assemblies from system dependencies and providers.
Game assemblies and untracked mod assets are excluded.
See [Install](../installation/linux.md#fedora) for mod attachment and service setup.
`just tools-test tools/release` tests staging and failures without the game;
`just package-test fedora` checks real archives with native ELF fixtures.
