"""Keep route discovery failures visible when router structure changes."""

import pytest

from tools import ROOT
from tools.docs.api_docs import api_routes


def test_unresolved_route_path_fails() -> None:
    with pytest.raises(ValueError, match='unresolved API route path'):
        api_routes({ROOT / 'slopd/src/api/router.rs': 'fn root_routes() { router.route(UNKNOWN, get(handler)) }'})


def test_unknown_route_family_fails() -> None:
    with pytest.raises(ValueError, match='unknown API route family'):
        api_routes({ROOT / 'slopd/src/api/router.rs': 'fn new_routes() { router.route("/api/new", get(handler)) }'})


@pytest.mark.parametrize(
    ('family', 'scope'),
    [('root_routes', 'root-only'), ('scoped_routes', 'scoped'), ('followup_routes', 'scoped')],
)
def test_route_families_retain_explicit_access_labels(family: str, scope: str) -> None:
    routes = api_routes(
        {ROOT / 'slopd/src/api/router.rs': f'fn {family}() {{ router.route("/api/example", post(handler)) }}'}
    )
    assert len(routes) == 1
    assert routes[0].scope == scope
