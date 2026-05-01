"""pytest fixtures for the Fable-transpiled F# test suite.

Fable Python codegen wraps the test functions in their original F# module
namespace (e.g. `Fable.TypedJson.Tests.Codec.test_*`) but emits each as a
top-level Python function in a `test_*.py` file. pytest's default
discovery (`test_*` functions in `test_*.py` files) picks them up.

This conftest just makes sure the build/python_test directory is on
`sys.path` so the inter-module relative imports work.
"""

import sys
from pathlib import Path

# build/python_test is where Fable wrote the modules. Adding the parent
# (build/) lets `from python_test.X import Y`-style relative imports work
# if any are emitted; the test files themselves are siblings.
HERE = Path(__file__).parent
sys.path.insert(0, str(HERE))
