"""Validate rejection of unsupported shader drift without copying game source."""

import unittest

from prepare import adapt
from prepare import replace_once


class ShaderContractTests(unittest.TestCase):
    def test_rejects_ambiguous_resource_rewrite(self):
        with self.assertRaises(ValueError):
            replace_once('binding binding', 'binding', 'uniform')

    def test_rejects_unknown_shader_version(self):
        with self.assertRaises(ValueError):
            adapt('#version 460', 'vert')

    def test_rejects_unsupported_stage(self):
        with self.assertRaises(ValueError):
            adapt('#version 450', 'compute')

    def test_rejects_changed_push_constant_contract(self):
        with self.assertRaises(ValueError):
            adapt('#version 450\nlayout(push_constant) uniform Changed {} pc;', 'vert')


if __name__ == '__main__':
    unittest.main()
