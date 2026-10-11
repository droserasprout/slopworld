# Arch package construction uses makepkg and nested Bubblewrap when run as root.
FROM docker.io/library/archlinux:base-devel
RUN pacman -Syu --noconfirm --needed rust alsa-lib lld protobuf git python python-pytest bubblewrap \
    && pacman -Scc --noconfirm
ENV CARGO_HOME=/usr/local/cargo
WORKDIR /source
