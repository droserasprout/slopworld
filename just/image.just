# Build the Arch release compiler image
[group('image')]
image-arch: (_image-build 'arch')

# Build the Debian release compiler image
[group('image')]
image-debian: (_image-build 'debian')

# Build the Fedora release compiler image
[group('image')]
image-fedora: (_image-build 'fedora')

[private]
[positional-arguments]
[group('image')]
_image-build what:
    exec "$CONTAINER" build $CONTAINER_BUILD_ARGS -t "slopworld-$1-release" -f "packaging/$1/Dockerfile" "packaging/$1"
