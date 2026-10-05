"""Reject definitions that cannot produce valid bindings or unambiguous adapters."""

import pytest

from tools.protocol import wire_contract
from tools.protocol.protobuf_http import validate_route_precedence


@pytest.mark.parametrize('route', [None, {}, {'path': '/api/test', 'methods': ['GET'], 'scope': 'scoped'}])
def test_incomplete_routes_fail_with_validation_error(route):
    data = wire_contract.load()
    data['protocol']['http']['routes'] = {'broken': route}
    with pytest.raises(ValueError, match='invalid route'):
        wire_contract.validate(data)


@pytest.mark.parametrize('payloads', [None, {}, {'GET': ['Empty']}, {'GET': ['Empty', 1]}])
def test_malformed_payload_mapping_fails(payloads):
    data = wire_contract.load()
    data['protocol']['http']['routes']['health']['protobuf'] = payloads
    with pytest.raises(ValueError, match='Protobuf'):
        wire_contract.validate(data)


@pytest.mark.parametrize('name,value', [('terminal_min_cols', -1), ('terminal_max_cols', 65536), ('other', 2147483648)])
def test_constants_fit_both_target_languages(name, value):
    data = wire_contract.load()
    data['protocol']['constants'][name] = value
    with pytest.raises(ValueError, match='integer must be'):
        wire_contract.validate(data)


@pytest.mark.parametrize('names', [['foo_bar', 'foo-bar'], ['_1'], ['bad.name']])
def test_invalid_or_colliding_generated_identifiers_fail(names):
    with pytest.raises(ValueError, match='identifier'):
        wire_contract.validate_names(names, 'example')


def test_string_controls_are_escaped_for_each_language():
    assert wire_contract.rust_string('\r\t\x01') == '"\\r\\t\\u{1}"'
    assert wire_contract.cs_string('\r\t\x01') == '"\\r\\t\\u0001"'


def test_overlapping_mixed_routes_are_rejected_but_static_precedence_is_valid():
    with pytest.raises(ValueError, match='ambiguous'):
        validate_route_precedence([('GET', '/api/:id/view'), ('GET', '/api/current/:action')])
    validate_route_precedence([('GET', '/api/current/view'), ('GET', '/api/:id/:action')])
    validate_route_precedence([('GET', '/api/:id/view'), ('POST', '/api/current/:action')])
