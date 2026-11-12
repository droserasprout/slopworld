"""Host connection snapshots; descriptor addresses and credentials are read together."""

import json
import os
import sys
import tomllib
from collections.abc import Mapping
from pathlib import Path
from urllib.parse import urlsplit


def connection(env: Mapping[str, str], descriptor_text: str | None = None) -> dict[str, str]:
    if descriptor := env.get('SLOPWEB_ENDPOINT_FILE'):
        values = tomllib.loads(descriptor_text if descriptor_text is not None else Path(descriptor).read_text())
        url, token = values.get('url'), values.get('token')
    else:
        url = env.get('SLOPD_URL')
        token = (
            Path(env['SLOPWEB_TOKEN_FILE']).read_text().strip()
            if env.get('SLOPWEB_TOKEN_FILE')
            else env.get('SLOPD_TOKEN')
        )
    if not isinstance(url, str) or not isinstance(token, str) or not url or not token:
        raise ValueError('A daemon URL and token must be supplied together')
    parsed = urlsplit(url)
    if parsed.scheme not in ('http', 'https') or not parsed.hostname or parsed.username or parsed.password:
        raise ValueError('Daemon URL must use HTTP or HTTPS without embedded credentials')
    return {'endpoint': url, 'token': token}


if __name__ == '__main__':
    try:
        print(json.dumps(connection(os.environ, sys.stdin.read() if '--stdin' in sys.argv else None)))
    except (OSError, ValueError):
        sys.exit('Host endpoint unavailable')
