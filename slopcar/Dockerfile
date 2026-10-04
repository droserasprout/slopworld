# syntax=docker/dockerfile:1
# Debian forky publishes linux/amd64 and linux/arm64 natively, so the launcher no longer pins a
# platform and Apple Silicon runs the image without emulation. slopd's sandbox skeleton reads the
# host's own usr-merge layout at runtime, so this multiarch tree works the same as Arch's did.

FROM rust:1-trixie@sha256:620dbcd124499c59e2406d3741574b5c5838cf9eb9656f0c3a03948f79b02959 AS build
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
RUN touch src/main.rs src/bin/slopctl.rs \
    && cargo build --release --locked --bin slopd --bin slopctl

# Forky supplies tmux 3.7c and Node 24; npm's script allowlist needs npm 12.
FROM debian:forky-slim@sha256:7bb96d9b4ccd7f46f54853c258a56df0c6b628a63e65c2317959da18fd36932c

ARG CLAUDE_CODE_VERSION=2.1.245
ARG CODEX_VERSION=0.149.1
ARG NPM_VERSION=12.0.2
ARG OPENCODE_VERSION=1.18.23
ARG PI_VERSION=0.84.3

RUN apt-get update && apt-get install -y --no-install-recommends \
        bubblewrap \
        ca-certificates \
        git \
        highlight \
        less \
        libasound2t64 \
        micro \
        nodejs \
        npm \
        openssh-client \
        passt \
        procps \
        python3 \
        ripgrep \
        tmux=3.7c-1 \
    && npm install --global "npm@${NPM_VERSION}" \
    && npm install --global \
        --allow-scripts=@anthropic-ai/claude-code,opencode-ai,@google/genai,protobufjs,koffi \
        "@anthropic-ai/claude-code@${CLAUDE_CODE_VERSION}" \
        "@openai/codex@${CODEX_VERSION}" \
        "opencode-ai@${OPENCODE_VERSION}" \
        "@earendil-works/pi-coding-agent@${PI_VERSION}" \
    && npm cache clean --force \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build --chmod=755 /build/slopd/target/release/slopd /usr/local/bin/slopd
COPY --from=build --chmod=755 /build/slopd/target/release/slopctl /usr/local/bin/slopctl

RUN useradd --create-home --uid 1000 --shell /bin/bash slop \
    && install -d -o slop -g slop -m 0700 \
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
    && chown -R slop:slop /home/slop

COPY --chmod=755 slopcar/entrypoint.sh /usr/local/bin/slopcar-entrypoint
COPY --chmod=755 slopcar/doctor.sh /usr/local/bin/slopcar-doctor

ENV HOME=/home/slop \
    USER=slop \
    LOGNAME=slop \
    SHELL=/bin/bash \
    PATH=/home/slop/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/bin \
    XDG_CACHE_HOME=/home/slop/.cache \
    XDG_CONFIG_HOME=/home/slop/.config \
    XDG_DATA_HOME=/home/slop/.local/share \
    XDG_STATE_HOME=/home/slop/.local/state \
    SLOPD_RUNTIME=slopcar \
    SLOPD_LOG=slopd=info

USER 1000:1000
WORKDIR /home/slop
EXPOSE 7718
ENTRYPOINT ["slopcar-entrypoint"]
CMD ["slopd"]
HEALTHCHECK --interval=5s --timeout=3s --start-period=10s --retries=6 CMD ["slopctl", "status"]
