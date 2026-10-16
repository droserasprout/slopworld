"""Validate task scopes and select Just's private implementation recipes.

Just owns commands and prerequisite ordering. This module only maps the public
verb/component interface to those recipes; it never implements build policy.
It uses the standard library so daemon builds do not need the uv environment.
"""

import argparse
import os
import sys
from dataclasses import dataclass

from tools.utils import run
from tools.utils import run_main


@dataclass(frozen=True)
class Action:
    description: str
    targets: dict[str, tuple[str, ...]]
    default: str | None = 'all'


def components(operation: str, *owners: str) -> dict[str, tuple[str, ...]]:
    return {owner: (f'_{owner}::{operation}',) for owner in owners}


ACTIONS = {
    'build': Action(
        'Build daemon and mod by default. Sidecar commands live under just sidecar.',
        {
            'all': ('_build-all',),
            **components('build', 'daemon', 'mod'),
        },
    ),
    'install': Action(
        'Build and install daemon and mod by default.',
        {
            'all': ('_install-all',),
            **components('install', 'daemon', 'mod'),
        },
    ),
    'package': Action(
        'Build one native package without installing it. Requires game assemblies and distribution packaging tools.',
        dict.fromkeys(('arch', 'debian', 'fedora'), ('_package',)),
        default=None,
    ),
    'format': Action(
        'Apply formatting to all source components by default.',
        {
            'all': ('_format-all',),
            **components('format', 'daemon', 'mod', 'tools'),
        },
    ),
    'lint': Action(
        'Check formatting and static analysis without rewriting sources.',
        {
            'all': ('_lint-all',),
            **components('lint', 'daemon', 'mod', 'tools'),
        },
    ),
    'test': Action(
        'Run regular game-free tests. Sidecar and benchmarks have their own groups. Package tests require a distribution.',
        {
            'all': ('_test-all',),
            **components('test', 'daemon', 'mod', 'tools'),
            'pager': ('_tools::test-pager',),
            'package': ('_test-package',),
        },
    ),
    'coverage': Action(
        'Collect Python, daemon Rust and game-free C# coverage by default.',
        {
            'all': ('_coverage::all',),
            **components('coverage', 'daemon', 'mod', 'tools'),
            'summary': ('_coverage::summary',),
            'summary-rust': ('_coverage::summary-rust',),
            'summary-mod': ('_coverage::summary-mod',),
        },
    ),
    'check': Action(
        'Run one focused validation. mod-build requires game assemblies.',
        {
            'protocol': ('_generate::check-protocol',),
            'licenses': ('_generate::check-licenses',),
            'themes': ('_mod::check-themes',),
            'mod-build': ('_mod::check-build',),
            'daemon-format': ('_daemon::check-format',),
            'requirements': ('_tools::check-requirements',),
        },
        default=None,
    ),
    'fix': Action('Apply automatically correctable Python lint fixes.', components('fix', 'tools'), default='tools'),
    'ci': Action(
        'Run every CI stage by default, or select the same stage used by GitHub Actions.',
        {
            stage: (f'_ci::{stage}',)
            for stage in (
                'all',
                'protocol',
                'format-mod',
                'tools',
                'tests',
                'rust',
                'coverage-rust',
                'coverage-mod',
                'sidecar-rust',
                'sidecar-tools',
                'bench',
            )
        },
    ),
    'generate': Action(
        'Regenerate outputs from reviewed inputs; all stages notices last. Does not update locks.',
        {
            output: (f'_generate::{output}',)
            for output in (
                'all',
                'protocol',
                'api-docs',
                'loading-font',
                'icons',
                'appicon',
                'favicon',
                'daemon-licenses',
                'sidecar-licenses',
                'licenses',
            )
        },
    ),
    'clean': Action(
        'Remove ordinary build output; all also removes coverage reports.',
        {
            'all': ('_clean-all',),
            **components('clean', 'daemon', 'mod', 'coverage'),
        },
    ),
    'uninstall': Action(
        'Remove installed files while preserving config and saves.',
        {
            'all': ('_uninstall-all',),
            **components('uninstall', 'daemon', 'mod'),
        },
    ),
    'deps-update': Action(
        'Update locked graphs. Updating all also regenerates and stages distribution notices.',
        {
            'all': ('_deps::update',),
            **{owner: (f'_deps::{owner}',) for owner in ('tools', 'daemon', 'mod', 'sidecar', 'bench')},
        },
    ),
    'deps-restore': Action(
        'Restore reviewed locks without upgrading dependencies.',
        {
            'all': ('_tools::restore', '_mod::restore', '_mod::restore-tools'),
            'tools': ('_tools::restore',),
            'mod': ('_mod::restore',),
            'dotnet': ('_mod::restore-tools',),
        },
    ),
}


def main() -> None:
    action_parser = argparse.ArgumentParser(add_help=False)
    action_parser.add_argument('action', choices=ACTIONS)
    selected = action_parser.parse_args(sys.argv[1:2])
    arguments = sys.argv[2:]
    action = ACTIONS[selected.action]
    parser = argparse.ArgumentParser(prog='just ' + selected.action.replace('-', ' '), description=action.description)
    parser.add_argument(
        'what', choices=action.targets, nargs='?' if action.default is not None else None, default=action.default
    )
    # Component test flags belong to Cargo, the C# harness, or pytest.
    selection = parser.parse_args(arguments[:1])
    extra = arguments[1:]
    if (
        selected.action == 'test'
        and selection.what == 'package'
        and (len(extra) != 1 or extra[0] not in ('arch', 'debian', 'fedora'))
    ):
        parser.error('package tests require one distribution: arch, debian or fedora')
    if extra and (selected.action != 'test' or selection.what not in ('daemon', 'mod', 'tools', 'package')):
        parser.error('unexpected arguments: ' + ' '.join(extra))
    targets = action.targets[selection.what]
    # Just recomputes these build-derived variables instead of reading environment
    # values. Preserve this invocation's values across the scope-selection boundary.
    settings = [f'{name}={os.environ[name]}' for name in ('CARGOFLAGS', 'TARGET', 'RUNNER', 'SLOPCTL')]
    recipe_arguments = [selection.what] if selected.action == 'package' else extra
    run([os.environ['JUST_CMD'], '--justfile', os.environ['JUST_ROOT'], *settings, *targets, *recipe_arguments])


if __name__ == '__main__':
    run_main(main)
