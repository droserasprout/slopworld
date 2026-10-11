# Compile against Fedora 43 libraries.
FROM registry.fedoraproject.org/fedora:43
RUN dnf install -y cargo rust gcc alsa-lib-devel lld pkgconf-pkg-config \
        protobuf-compiler git python3 python3-pytest rpm-build redhat-rpm-config cpio \
    && dnf clean all
ENV CARGO_HOME=/usr/local/cargo
WORKDIR /source
