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


def test_provider_routes_remain_documented_as_root_only() -> None:
    routes = api_routes(
        {
            ROOT
            / 'slopd/src/api/router.rs': 'fn root_provider_routes() { router.route("/api/planner/action", post(planner_action)) }'
        }
    )
    assert len(routes) == 1
    assert routes[0].scope == 'root-only'
