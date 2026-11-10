"""Reduce local assembly metadata to a small, shareable, binary-free snapshot."""

import collections
import datetime
import json
import sys
from pathlib import Path


def main():
    inventory = json.loads(Path('out/inventory.json').read_text())
    assemblies = []
    for assembly in inventory['assemblies']:
        imports = collections.Counter(row['library'] for row in assembly['imports'])
        hazards = collections.Counter(row['category'] for row in assembly['hazards'])
        if not imports and not hazards:
            continue
        assemblies.append(
            {key: assembly[key] for key in ('owner', 'file', 'sha256', 'version', 'mvid')}
            | {'imports': dict(sorted(imports.items())), 'callSites': dict(sorted(hazards.items()))}
        )
    game = next(a for a in inventory['assemblies'] if a['file'] == 'Assembly-CSharp.dll')
    result = {
        'schemaVersion': 1,
        'inspection': inventory['inspection'],
        'gameVersion': inventory['gameVersion'],
        'runtime': inventory['runtime'],
        'assemblies': assemblies,
        'gameDynamicMethodConstructors': [
            site for site in game['hazards'] if site['target'] == 'System.Reflection.Emit.DynamicMethod::.ctor'
        ],
        'gameThreadStarts': [site for site in game['hazards'] if site['target'] == 'System.Threading.Thread::Start'],
    }
    Path('out/summary.json').write_text(json.dumps(result, indent=2) + '\n')
    print(f'Inspected {len(inventory["assemblies"])} managed assemblies; wrote out/summary.json')
    if '--record' in sys.argv:
        browser = json.loads(Path('out/browser-probe.json').read_text())
        host = json.loads(Path('out/host-probe.json').read_text())
        if not browser['completed'] or not browser['result']['browser']:
            raise ValueError('A completed actual browser probe is required')
        observed = browser['result']
        checks = {}
        for name in (
            'dynamicMethod',
            'harmony',
            'tcpClient',
            'socketsHttpHandler',
            'socketsHttpModOptions',
            'threadStart',
            'socketsHttpSend',
            'browserHttpSendControl',
            'modAssemblyLoad',
        ):
            check = observed[name]
            checks[name] = {
                'ok': check['ok'],
                'value': check.get('value'),
                'exception': check.get('exception', '').split('\n', 1)[0] or None,
            }
            for key in ('status', 'bytes'):
                if key in check:
                    checks[name][key] = check[key]
        evidence = {
            'observedDate': datetime.datetime.now(datetime.UTC).date().isoformat(),
            'gameVersion': inventory['gameVersion'],
            'gameRuntime': inventory['runtime']['runtimeOptions']['includedFrameworks'],
            'browser': {
                key: observed[key]
                for key in ('framework', 'architecture', 'browser', 'dynamicCodeSupported', 'dynamicCodeCompiled')
            },
            'configuration': 'Debug browser interpreter; no WASM threads or AOT enabled; trimming disabled',
            'checks': checks,
            'hostControl': {
                'framework': host['framework'],
                'architecture': host['architecture'],
                'harmony': host['harmony'],
            },
            'inputs': [
                {key: a[key] for key in ('file', 'version', 'sha256')}
                for a in inventory['assemblies']
                if a['file'] in ('Assembly-CSharp.dll', 'RimWorld.dll', 'Waylith.dll', '0Harmony.dll', 'SlopWorld.dll')
            ],
            'gameDynamicMethodConstructorCount': len(result['gameDynamicMethodConstructors']),
            'limitation': 'Capability tests, not colony startup. Mod assembly load does not initialize or exercise the mod; thread Start is not a completion test. HTTP reads same-origin static index.html only. Full raw inventories and stacks stay in ignored out/.',
        }
        Path('evidence').mkdir(exist_ok=True)
        Path('evidence/result.json').write_text(json.dumps(evidence, indent=2) + '\n')
        print('Wrote curated evidence/result.json')


if __name__ == '__main__':
    main()
