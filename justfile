# SlopWorld developer commands. Settings accept environment or just NAME=value overrides.
set shell := ["bash", "-euc"]
set quiet

# List developer commands (default)
[group('Common commands')]
help:
    @"{{ just_executable() }}" --list --unsorted

import 'just/config.just'
import 'just/popular.just'
import 'just/build.just'
import 'just/release.just'
import 'just/test.just'
import 'just/bench.just'
import 'just/generate.just'
import 'just/quality.just'
import 'just/install.just'
import 'just/sidecar.just'
import 'just/misc.just'
import 'just/rimworld.just'
