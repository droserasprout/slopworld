"""Repository development tools; run modules from the checkout with python -m.

ROOT owns checkout paths independently of a module's subpackage depth. These
commands operate on repository sources and assets rather than an installed copy.
"""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
