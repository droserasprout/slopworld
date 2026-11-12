"""Own launcher syntax and local informational commands, without preparing runtime state."""

import argparse
import os
import sys
from pathlib import Path
from urllib.parse import urlsplit

import application_paths


def version() -> str:
    bundle = Path(__file__).resolve().parent
    if (bundle / 'VERSION').is_file():
        return (bundle / 'VERSION').read_text().strip()
    manifest = bundle.parent / 'slopd/Cargo.toml'
    if manifest.is_file():
        sys.path.insert(0, str(bundle.parent))
        from tools.version.metadata import package_version
        from tools.version.resolve import from_git

        return from_git(package_version(bundle.parent), bundle.parent)
    return 'unknown'


def public_origin(value: str) -> str:
    """Accept one explicit browser origin, never proxy-supplied forwarding metadata."""
    try:
        uri = urlsplit(value)
        host = uri.hostname or ''
        host = f'[{host}]' if ':' in host else host
        port = f':{uri.port}' if uri.port is not None and uri.port != 443 else ''
        if not host or value != f'https://{host}{port}' or uri.username or uri.password:
            raise ValueError('not an HTTPS origin')
    except ValueError as error:
        raise argparse.ArgumentTypeError('Expected an exact HTTPS origin without a path or credentials') from error
    return value


def parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog='slopweb', description='Launch the local browser workspace', allow_abbrev=False
    )
    parser.add_argument('-V', '--version', action='version', version='slopweb ' + version())
    parser.add_argument('--game', type=Path, help='RimWorld install (SLOPWORLD_GAME, game.toml, usual locations)')
    parser.add_argument('--port', type=int, default=8787)
    parser.add_argument('--endpoint', help='Saved daemon endpoint name (default: local)')
    parser.add_argument('--url', help='Daemon URL; requires --token-file')
    parser.add_argument('--token-file', help='Read a host-only daemon token file')
    parser.add_argument('--cache', type=Path, help='Private browser runtime cache (default: XDG_CACHE_HOME/slopweb)')
    parser.add_argument(
        '--profile', type=Path, help='Profile directory (default: SLOPWORLD_PROFILE or endpoint profile)'
    )
    parser.add_argument('--sidecar', action='store_true', help='Use sidecar paths')
    parser.add_argument('--prepare', action='store_true', help='Prepare local game assets and exit')
    parser.add_argument('--rebuild', action='store_true', help='Refresh local game data after a game update')
    parser.add_argument(
        '--public-origin', type=public_origin, help='Exact HTTPS origin of an authenticated reverse proxy'
    )
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument(
        '--interpreted', action='store_true', help='Use the interpreter (default; overrides SLOPWEB_AOT_SDK)'
    )
    mode.add_argument('--aot', action='store_true', help='Enable mixed AOT using wasm-tools')
    parser.add_argument(
        '--aot-sdk',
        type=Path,
        help='Select the .NET 10 SDK with wasm-tools (default: SLOPWEB_AOT_SDK or dotnet on PATH)',
    )
    commands = parser.add_subparsers(dest='command')
    paths = commands.add_parser('paths', help='Print an absolute path without loading runtime state')
    paths.add_argument('name', choices=application_paths.PATH_NAMES)
    for target in (paths,):
        target.add_argument('--game', type=Path, default=argparse.SUPPRESS)
        target.add_argument('--cache', type=Path, default=argparse.SUPPRESS)
        target.add_argument('--endpoint', default=argparse.SUPPRESS)
        target.add_argument('--profile', type=Path, default=argparse.SUPPRESS)
        target.add_argument('--sidecar', action='store_true', default=argparse.SUPPRESS)
    completions = commands.add_parser('completions', help='Print a shell completion script')
    completions.add_argument('shell', choices=('bash', 'zsh', 'fish'))
    return parser


def completion_script(shell: str) -> str:
    """Render completions from the parser's options and local command choices."""
    command = parser()
    options = [flag for action in command._actions for flag in action.option_strings]
    words = ' '.join([*options, 'paths', 'completions'])
    paths = ' '.join(application_paths.PATH_NAMES)
    if shell == 'bash':
        return f"""_slopweb() {{
    COMPREPLY=()
    local words='{words}' previous="${{COMP_WORDS[COMP_CWORD-1]}}" word skip=0
    case "$previous" in
        --game|--profile|--cache|--aot-sdk) mapfile -t COMPREPLY < <(compgen -d -- "${{COMP_WORDS[COMP_CWORD]}}"); return ;;
        --token-file) mapfile -t COMPREPLY < <(compgen -f -- "${{COMP_WORDS[COMP_CWORD]}}"); return ;;
        --endpoint|--url|--public-origin|--port) return ;;
    esac
    for word in "${{COMP_WORDS[@]:1:COMP_CWORD-1}}"; do
        if ((skip)); then skip=0; continue; fi
        case "$word" in
            --game|--profile|--cache|--aot-sdk|--token-file|--endpoint|--url|--public-origin|--port) skip=1 ;;
            paths) words='{paths}'; break ;;
            completions) words='bash zsh fish'; break ;;
        esac
    done
    COMPREPLY=( $(compgen -W "$words" -- "${{COMP_WORDS[COMP_CWORD]}}") )
}}
complete -F _slopweb slopweb
"""
    if shell == 'zsh':
        specs = []
        for action in command._actions:
            for flag in action.option_strings:
                suffix = ':value:_files' if action.nargs != 0 else ''
                help_text = (action.help or '').replace("'", '').replace('[', '').replace(']', '')
                specs.append(f"        '{flag}[{help_text}]{suffix}'")
        arguments = ' \\\n'.join(specs)
        return f"""#compdef slopweb
_slopweb() {{
    local context state state_descr line
    typeset -A opt_args
    _arguments -C \\
{arguments} \\
        '1:command:->command' \\
        '*:value:->value'
    case $state in
        command) compadd paths completions ;;
        value)
            case $line[1] in
                paths) compadd {paths} ;;
                completions) compadd bash zsh fish ;;
            esac ;;
    esac
}}
_slopweb "$@"
"""
    lines = ['complete -c slopweb -f', "complete -c slopweb -n '__fish_use_subcommand' -a 'paths completions'"]
    for action in command._actions:
        for flag in action.option_strings:
            kind, name = ('l', flag[2:]) if flag.startswith('--') else ('s', flag[1:])
            lines.append(f'complete -c slopweb -{kind} {name}' + (' -r -F' if action.nargs != 0 else ''))
    lines += [
        f"complete -c slopweb -n '__fish_seen_subcommand_from paths' -a '{paths}'",
        "complete -c slopweb -n '__fish_seen_subcommand_from completions' -a 'bash zsh fish'",
    ]
    return '\n'.join(lines) + '\n'


def use_aot(options: argparse.Namespace) -> bool:
    """An explicit interpreter request overrides SDK environment configuration."""
    if options.interpreted:
        if options.aot_sdk:
            raise ValueError('--interpreted cannot be combined with --aot-sdk')
        return False
    return bool(options.aot or options.aot_sdk or os.environ.get('SLOPWEB_AOT_SDK'))
