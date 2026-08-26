# Install

The current complete install path is Linux:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game make install
slopworld
```

For the experimental macOS worker, install Docker Desktop, then build the Debian sidecar and
install the native mod:

```sh
brew install make
gmake mac-setup
open -a Docker
gmake mac
```

The default `MAC_RIMWORLD` is Steam's `RimWorldMac.app`; override it for another install. The
first start creates a random daemon token and publishes port 7718 only on Mac loopback. `mac-run`
launches the native game directly because the Linux `slopworld` launcher is not used on macOS.
See the [sidecar contract](../../slopcar/README.md) for workspace and credential mounts.
