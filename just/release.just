set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Update product versions for a dated changelog release
bump-version version:
    ${PYTHON} -m tools.version.bump {{ quote(version) }}

# Validate, build and publish a versioned release
publish:
    ${PYTHON} -m tools.release.publish publish

# Validate tagged main, regenerate, check, test and package locally
package:
    ${PYTHON} -m tools.release.publish package
