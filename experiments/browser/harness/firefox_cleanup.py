"""Recover only temporary host fixtures named in a Firefox harness report."""

import json
import re
import sys
import tomllib
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import urljoin
from urllib.request import Request
from urllib.request import urlopen


def fields(data):
    offset = 0

    def number():
        nonlocal offset
        value = 0
        for shift in range(0, 70, 7):
            byte = data[offset]
            offset += 1
            value |= (byte & 127) << shift
            if not byte & 128:
                return value
        raise ValueError('Invalid protobuf')

    result = {}
    while offset < len(data):
        tag = number()
        wire = tag & 7
        if wire == 0:
            result[tag >> 3] = number()
        elif wire == 2:
            size = number()
            offset += size
        elif wire in (1, 5):
            offset += 8 if wire == 1 else 4
        else:
            raise ValueError('Invalid protobuf wire type')
    return result


def cleanup(report_path, descriptor):
    report = json.loads(Path(report_path).read_text())
    if report.get('backend') != 'Firefox WebDriver':
        raise ValueError('Expected a Firefox harness report')
    for name in report.get('ownedSessions', report.get('fixture', {}).get('created', [])):
        if not re.fullmatch(r'slopweb-bench-[0-9a-f]{8}', name):
            raise ValueError('Refusing an unrecognized fixture name')
        url = urljoin(descriptor['url'], '/api/sessions/' + name)

        def request(method, target=url):
            try:
                with urlopen(
                    Request(target, method=method, headers={'x-slop-token': descriptor['token']}), timeout=10
                ) as response:
                    return response.status, response.read()
            except HTTPError as error:
                if error.code == 404:
                    return 404, b''
                raise RuntimeError('Fixture recovery HTTP ' + str(error.code)) from None

        status, body = request('GET')
        if status == 404:
            continue
        metadata = fields(body)
        if metadata.get(26) != 1 or metadata.get(27) != 1:
            raise ValueError('Refusing a non-temporary or non-host fixture')
        request('DELETE')
        if request('GET')[0] != 404:
            raise RuntimeError('Owned fixture still exists')
        print('Removed owned fixture ' + name)


if __name__ == '__main__':
    import os

    config = Path(os.environ.get('XDG_CONFIG_HOME', Path.home() / '.config')) / 'slopworld'
    cleanup(sys.argv[1], tomllib.loads((config / 'endpoints/local.toml').read_text()))
