# Release settings, imported only by the root justfile.
export CONTAINER_BUILD_ARGS := env("CONTAINER_BUILD_ARGS", '')
export CONTAINER_RUN_ARGS := env("CONTAINER_RUN_ARGS", '')
export CONTAINER := env("CONTAINER", 'podman')
export GH := env("GH", 'gh')
export RELEASE_DIR := env("RELEASE_DIR", '')
export ARCH_DIR := env("ARCH_DIR", 'dist/arch')
export DEB_DIR := env("DEB_DIR", 'dist/debian')
export RPM_DIR := env("RPM_DIR", 'dist/fedora')
