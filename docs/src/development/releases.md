# Packaging and releases

Build a package for your distribution or publish a versioned GitHub release.
Start with [Build from source](build.md) for the toolchain and checks.

## Versioned GitHub releases

### Requirements

Use an Arch Linux x86_64 host with RimWorld installed, `makepkg`, `fakeroot`,
`pacman`, and Podman. To use Docker instead, set `CONTAINER=docker`.
Root builds also require Bubblewrap to run `makepkg` as an ordinary UID.

Both release commands require a clean checkout on `main` with a
`{{#constant release_tag_format}}` tag at HEAD.

### Prepare a version

1. Finish the dated `CHANGELOG.md` entry. It supplies the release description.
2. Run `just release-bump-version {{#constant release_version}}`, then
   `just generate-all`. Review and commit the changes on `main`.
3. Tag the commit `{{#constant release_tag}}`. HEAD must have exactly one release
   version tag, matching the product versions and any `VERSION` override.

### Build packages

```sh
just release-package
```

This runs generation and validation, then builds the daemon archive, mod ZIP,
Arch, Debian, and Fedora packages, checksums, and release notes in
`dist/{{#constant release_version}}/`. Set `RELEASE_DIR` to change the output directory.

If generation changes tracked files, review and commit them, then tag the final
release commit before rerunning. Release commands never commit changes or move tags.

<a id="package-contents-and-versions"></a>

### Publish

Push the tagged commit to GitHub and authenticate with `gh auth login`.
Check the GitHub CLI's selected repository; set `GH_REPO=owner/repo` to choose
another destination. Then run:

```sh
just release-publish
```

This builds and validates the packages before publishing, so a separate
`release-package` run is optional. Existing releases are never replaced.
If publication creates a remote tag but fails before creating the release,
rerun from the same commit. If the release exists with incomplete assets,
inspect and repair it explicitly.

Release tag pushes also trigger sidecar image publication.

## Distribution packages

These commands build a local package without publishing or installing it.
Each needs the [build prerequisites](build.md#prerequisites) and native Linux
RimWorld assemblies. Commit changes before packaging to record a reproducible
source revision. `VERSION` overrides the build version.

Build Debian/Ubuntu and Fedora packages on the oldest distribution release you
intend to support; their library requirements come from the build host.

### Arch packages

Install `base-devel` and `bubblewrap` on Arch, then run:

```sh
RIMWORLD=/path/to/RimWorld/game just package-arch
```

The package goes to `dist/arch/`; override with `ARCH_DIR`.

### Debian packages

Install `libasound2-dev` and `dpkg-dev` on Debian or Ubuntu, then run:

```sh
RIMWORLD=/path/to/RimWorld/game just package-debian
```

The package goes to `dist/debian/`; override with `DEB_DIR`.

### Fedora packages

Install `alsa-lib-devel`, `rpm-build`, and `redhat-rpm-config` on Fedora, then run:

```sh
RIMWORLD=/path/to/RimWorld/game just package-fedora
```

The package goes to `dist/fedora/`; override with `RPM_DIR`.
Follow [Linux installation](../installation/linux.md) to install a package,
attach the mod, and start the daemon.
