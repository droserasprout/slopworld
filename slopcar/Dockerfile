# syntax=docker/dockerfile:1
# Debian forky supports linux/amd64 and linux/arm64 natively.
# slopd's sandbox skeleton uses the container's usr-merge layout at runtime.

FROM rust:1-trixie AS build
WORKDIR /build/slopd
RUN apt-get update && apt-get install -y --no-install-recommends \
        libasound2-dev \
        pkg-config \
        protobuf-compiler \
    && rm -rf /var/lib/apt/lists/*
COPY slopd/Cargo.toml slopd/Cargo.lock ./
RUN mkdir src \
    && printf 'fn main() {}\n' > src/main.rs \
    && cargo build --release --locked --bin slopd \
    && rm -rf src
COPY shared/slopworld.proto /build/shared/slopworld.proto
COPY slopd/build.rs ./build.rs
COPY slopd/src ./src
COPY slopd/presets ./presets
ARG SLOPWORLD_BUILD_VERSION
RUN touch src/main.rs src/bin/slopctl.rs \
    && cargo build --release --locked --bin slopd --bin slopctl

# Forky supplies tmux 3.7c.
FROM debian:forky-slim AS runtime

RUN apt-get update && apt-get install -y --no-install-recommends \
        bubblewrap \
        ca-certificates \
        git \
        highlight \
        less \
        libasound2t64 \
        micro \
        openssh-client \
        passt \
        procps \
        python3 \
        ripgrep \
        systemd-standalone-sysusers \
        systemd-standalone-tmpfiles \
        tmux=3.7c-1 \
        util-linux \
    && rm -rf /var/lib/apt/lists/* /var/cache/debconf/*-old /var/lib/dpkg/*-old \
    && find /usr/lib/python3* -type d -name __pycache__ -prune -exec rm -rf '{}' +

# Empty mount destinations are traversable for any selected UID. Mounted credentials
# and persistent state retain their host permissions.
RUN useradd --create-home --uid 1000 --shell /bin/bash slop \
    && install -d -o slop -g slop -m 0755 \
        /home/slop/.cache \
        /home/slop/.claude \
        /home/slop/.codex \
        /home/slop/.config/gh \
        /home/slop/.config/opencode \
        /home/slop/.config/slopworld \
        /home/slop/.local/bin \
        /home/slop/.local/share/opencode \
        /home/slop/.local/share/slopworld \
        /home/slop/.local/state/opencode \
        /home/slop/.pi/agent \
        /home/slop/.ssh \
    && chown -R slop:slop /home/slop \
    && chmod 0755 /home/slop

# Copy the completed runtime filesystem once so package upgrades do not retain
# overwritten base files. Keep Debian's package database for user-supplied images.
FROM scratch
COPY --from=runtime / /
COPY --from=build --chmod=755 /build/slopd/target/release/slopd /usr/local/bin/slopd
COPY --from=build --chmod=755 /build/slopd/target/release/slopctl /usr/local/bin/slopctl
COPY LICENSE /usr/share/licenses/slopworld/LICENSE
COPY licenses/ /usr/share/licenses/slopworld/third-party/

COPY --chmod=755 slopcar/entrypoint.sh /usr/local/bin/slopcar-entrypoint
COPY --chmod=755 slopcar/doctor.sh /usr/local/bin/slopcar-doctor

ENV HOME=/home/slop \
    USER=slop \
    LOGNAME=slop \
    SHELL=/bin/bash \
    PATH=/home/slop/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin \
    XDG_CACHE_HOME=/home/slop/.cache \
    XDG_CONFIG_HOME=/home/slop/.config \
    XDG_DATA_HOME=/home/slop/.local/share \
    XDG_STATE_HOME=/home/slop/.local/state \
    SLOPD_RUNTIME=slopcar \
    SLOPD_LOG=slopd=info

USER root
WORKDIR /home/slop
EXPOSE 7718
ENTRYPOINT ["/usr/local/bin/slopcar-entrypoint"]
CMD ["slopd"]
HEALTHCHECK --interval=5s --timeout=3s --start-period=10s --retries=6 CMD ["/usr/bin/setpriv", "--reuid=slop", "--regid=slop", "--clear-groups", "--bounding-set=-all", "slopctl", "status"]
