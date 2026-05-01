"""Smoke test the Python backend transpiled from Fable.TypedJson.Python.

Run from repo root:  .venv/bin/python test_python/smoke.py
Assumes the package has been transpiled to build/python/.
"""

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).parent.parent
# Add `build/` so `import python.backend` resolves to the transpiled package.
sys.path.insert(0, str(REPO_ROOT / "build"))


def run() -> None:
    from python.backend import python  # pyright: ignore[reportMissingImports] # noqa: E402

    parsed = python.Parse('{"days": 7, "name": "alice"}')
    assert python.ContainsKey(parsed, "days"), "days key missing after parse"
    assert python.ContainsKey(parsed, "name"), "name key missing after parse"
    assert not python.ContainsKey(parsed, "missing"), "missing key reported present"
    assert python.Get(parsed, "days") == 7
    assert python.Get(parsed, "name") == "alice"

    fresh = python.NewMap()
    fresh = python.Put(fresh, "x", 1)
    fresh = python.Put(fresh, "y", "two")
    out = python.Stringify(fresh)
    # json.dumps preserves insertion order on CPython 3.7+
    assert out == '{"x": 1, "y": "two"}', f"unexpected stringify: {out}"
    print("PythonBackend smoke OK:", out)


if __name__ == "__main__":
    run()
