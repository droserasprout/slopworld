# Install

The current complete install path is Linux:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game make install
slopworld
```

For the experimental macOS worker, install and start Docker Desktop, then build and verify the
Debian sidecar:

```sh
./slopcar/slopcar build
./slopcar/slopcar doctor
./slopcar/slopcar start --workspace "$HOME/git"
```

The first start creates a random daemon token and publishes port 7717 only on Mac loopback. The
native mod installation/launcher is still manual; see the [sidecar contract](../../slopcar/README.md)
for workspace and credential mounts.
