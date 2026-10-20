# Packaging and releases

GitHub Actions tests, builds, attests, and publishes versioned releases without a
RimWorld installation. The mod compiles against the pinned RimRef package.
Start with [Build from source](build.md) for the toolchain and checks.

## Versioned GitHub releases

### Prepare a version

1. Finish the dated `CHANGELOG.md` entry. It supplies the release description.
2. Run `just release-bump-version {{#constant release_version}}`, then
   `just generate-all`. Review and commit the changes on `main`.
3. Tag the commit `{{#constant release_tag}}`. HEAD must have exactly one release
   version tag, matching the product versions and any `VERSION` override.
4. Push `main`, then the tag:

   ```sh
   git push origin main
   git push origin {{#constant release_tag}}
   ```

The **Release** workflow requires the tagged commit to belong to `origin/main`.
It resolves one commit, runs the reusable test workflow against it, and then
builds the daemon archive, mod ZIP, Arch, Debian, and Fedora packages. Native
packages compile in their distribution containers. The build consumes committed
artwork and checks protocol outputs, staged notices, and the documentation book.
Run generators and review their changes before tagging; CI does not regenerate
artwork with a different host renderer.

GitHub signs provenance attestations for the packages, `SHA256SUMS`, release
notes, and `release.json`. The manifest records the commit, version, and file
hashes. The workflow then publishes and attests the sidecar image for
`linux/amd64` and `linux/arm64`.

A separate publication job downloads the exact build artifact by ID, checks its
manifest and checksums, and verifies each file's attestation against the source
commit, tag, and release workflow. It creates the GitHub release without
rebuilding. A failed test, package build, image build, or attestation prevents
GitHub release publication. The image may already exist if a later step fails.

Existing GitHub releases are never replaced and missing remote tags are never
recreated by CI. Retry a failed tag run from the Actions page. If a release exists
with incomplete assets, inspect and repair it explicitly.

### Preview without publication

Run **Release** manually from the Actions page, or push the `release-ci` branch.
Previews run tests and the same packaging and attestation path, using a version
such as `0.0.7-preview-0123456789ab`. They upload a `release-<version>` workflow
artifact retained for 30 days. They do not publish GitHub releases or sidecar
images. Manual runs are previews even when a tag is selected.

### Verify a download

Install GitHub CLI, download the desired package and `SHA256SUMS`, then verify
its provenance against this repository and workflow:

```sh
gh attestation verify slopworld-{{#constant release_version}}-mod.zip \
  --repo droserasprout/slopworld \
  --signer-workflow droserasprout/slopworld/.github/workflows/release.yml \
  --source-ref refs/tags/{{#constant release_tag}} \
  --deny-self-hosted-runners
```

Add `--source-digest <commit>` to require a particular source commit. After
downloading all packages, run `sha256sum --check SHA256SUMS` as an additional
integrity check. Checksums alone do not establish who built a file.

The sidecar's provenance uses `.github/workflows/image.yml` as its signer:

```sh
gh attestation verify oci://ghcr.io/droserasprout/slopcar:{{#constant release_version}} \
  --repo droserasprout/slopworld \
  --signer-workflow droserasprout/slopworld/.github/workflows/image.yml \
  --source-ref refs/tags/{{#constant release_tag}} \
  --deny-self-hosted-runners
```

These attestations identify the build's source and workflow. They do not promise
byte-for-byte reproducibility: distribution repositories and some build images
still receive updates.

<a id="package-contents-and-versions"></a>

### Local release packages

`just release-package` retains the full local generation, validation, and
packaging flow. It requires a clean tagged commit on `main` (or a detached tagged
commit belonging to `main`), an x86_64 Linux build host, and Podman. Set
`CONTAINER=docker` to use Docker. All native packages build in containers;
RimRef supplies the mod references. Output goes to
`dist/{{#constant release_version}}/`; set `RELEASE_DIR` to override it.

`just release-publish` also builds locally and publishes through your authenticated
GitHub CLI. It does not produce hosted-build attestations. Prefer pushing a release
tag to use the verifiable CI flow. Local commands never commit changes or move tags.

<a id="publish"></a>

## Distribution packages

These commands build a local package without publishing or installing it.
Each needs the [build prerequisites](build.md#prerequisites) and its distribution's
native packaging tools. Set `MANAGED=rimref` to compile without a game installation,
or use the installed game's assemblies by default. Commit changes before
packaging to record the source revision. `VERSION` overrides the build version.

Build Debian/Ubuntu and Fedora packages on the oldest distribution release you
intend to support; their library requirements come from the build host.

### Arch packages

Install `base-devel` and `bubblewrap` on Arch, then run:

```sh
just MANAGED=rimref package-arch
```

The package goes to `dist/arch/`; override with `ARCH_DIR`.

### Debian packages

Install `libasound2-dev` and `dpkg-dev` on Debian or Ubuntu, then run:

```sh
just MANAGED=rimref package-debian
```

The package goes to `dist/debian/`; override with `DEB_DIR`.

### Fedora packages

Install `alsa-lib-devel`, `rpm-build`, and `redhat-rpm-config` on Fedora, then run:

```sh
just MANAGED=rimref package-fedora
```

The package goes to `dist/fedora/`; override with `RPM_DIR`.
Follow [Linux installation](../installation/linux.md) to install a package,
attach the mod, and start the daemon.
