"""Protocol verification must preserve both stale and missing checkout outputs."""

import tempfile
from pathlib import Path
from unittest.mock import patch

from tools.protocol import api_contract


def test_check_preserves_stale_and_missing_outputs():
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory) / 'checkout'
        generated = Path(directory) / 'generated'
        root.mkdir()
        generated.mkdir()
        (root / 'stale').write_bytes(b'old')
        (generated / 'stale').write_bytes(b'new')
        (generated / 'missing').write_bytes(b'new')
        with patch.object(api_contract, 'ROOT', root):
            assert api_contract.publish(generated, check=True) == [Path('missing'), Path('stale')]
            assert (root / 'stale').read_bytes() == b'old'
            assert not (root / 'missing').exists()
            api_contract.publish(generated, check=False)
            assert api_contract.publish(generated, check=True) == []
