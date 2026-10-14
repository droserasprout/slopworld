# Build a native package: arch, debian or fedora
[group('Maintain')]
[positional-arguments]
package what:
    python3 -m tools.recipes.dispatch package "$@"

[private]
_package what:
    ${PYTHON} -m tools.release.native {{ quote(what) }}

# Test real archives in a prepared distribution image
[private]
_test-package what:
    ${CONTAINER} run ${CONTAINER_RUN_ARGS} $(if [ {{ quote(what) }} = arch ]; then echo --privileged; fi) --rm --security-opt label=disable --volume "$PWD:$PWD:ro" --workdir "$PWD" "slopworld-{{ what }}-release" python3 -m pytest "tools/release/test_{{ what }}.py" -q -p no:cacheprovider

# Stage a mod for source packages
[private]
_stage-mod destination:
    ${PYTHON} -m tools.release.source_mod {{ quote(destination) }}
