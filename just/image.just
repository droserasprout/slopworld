set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Build the Arch release compiler image
arch: (_build 'arch')

# Build the Debian release compiler image
debian: (_build 'debian')

# Build the Fedora release compiler image
fedora: (_build 'fedora')

[private]
[positional-arguments]
_build what:
    exec $CONTAINER build $CONTAINER_BUILD_ARGS -t "slopworld-$1-release" -f "packaging/$1/Dockerfile" "packaging/$1"
