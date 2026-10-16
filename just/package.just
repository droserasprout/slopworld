# Build an Arch package
[group('package')]
package-arch: (_package-build 'arch')

# Build a Debian package
[group('package')]
package-debian: (_package-build 'debian')

# Build a Fedora package
[group('package')]
package-fedora: (_package-build 'fedora')

[private]
[group('package')]
_package-build what:
    uv run --locked python -m tools.release.native {{ quote(what) }}

# Test real archives in a prepared distribution image
[arg('what', pattern='^(arch|debian|fedora)$')]
[group('package')]
package-test what:
    "$CONTAINER" run ${CONTAINER_RUN_ARGS} $(if [ {{ quote(what) }} = arch ]; then echo --privileged; fi) --rm --security-opt label=disable --volume "$PWD:$PWD:ro" --workdir "$PWD" "slopworld-{{ what }}-release" python3 -m pytest "tools/release/test_{{ what }}.py" -q -p no:cacheprovider

# Stage a mod for source packages
[private]
[group('package')]
package-stage-mod destination:
    uv run --locked python -m tools.release.source_mod {{ quote(destination) }}
