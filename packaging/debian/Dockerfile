# Release binaries target Debian 13's libraries rather than the Arch build host.
FROM docker.io/library/rust:1-trixie
ARG APT_SANDBOX_USER=_apt
RUN apt-get -o APT::Sandbox::User=${APT_SANDBOX_USER} update && apt-get -o APT::Sandbox::User=${APT_SANDBOX_USER} install -y --no-install-recommends \
        dpkg-dev git libasound2-dev lld pkg-config protobuf-compiler python3 python3-pytest \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /source
