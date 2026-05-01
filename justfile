# Fable.TypedJson development tasks

# Development mode: use local Fable repo instead of dotnet tool
# Usage: just dev=true build
dev := "false"
fable_repo := justfile_directory() / "../Fable"
fable := if dev == "true" { "dotnet run --project " + fable_repo / "src/Fable.Cli" + " --" } else { "dotnet fable" }

# List available recipes
default:
    @just --list

# Clean build artifacts
clean:
    rm -rf apps _build build

# --- Build ---

# Build Fable.TypedJson + Fable.TypedJson.Beam to Erlang, then compile with rebar3
build: clean build-beam build-python

# Transpile core + BEAM shim to Erlang and compile with rebar3
build-beam:
    {{fable}} src/Fable.TypedJson --exclude Fable.Core --lang beam --outDir apps/fable_typed_json --noCache
    {{fable}} src/Fable.TypedJson.Beam --exclude Fable.Core --lang beam --outDir apps/fable_typed_json_beam --noCache
    rebar3 compile

# Transpile core + Python shim to Python (no further compile step needed)
build-python:
    {{fable}} src/Fable.TypedJson.Python --exclude Fable.Core --lang python --outDir build/python --noCache

# Type check via dotnet build
check:
    dotnet build src/Fable.TypedJson
    dotnet build src/Fable.TypedJson.Beam
    dotnet build src/Fable.TypedJson.Python

# Format source files
format:
    dotnet fantomas src/ test/

# Setup tooling
restore:
    dotnet tool restore
    dotnet paket install
    uv sync

# Build and check
all: check build

# --- Test ---

build_test_path := justfile_directory() / "build/tests"

# Run all backend test suites
test: test-beam test-python

# Build and run Fable.TypedJson tests on BEAM
test-beam: build-test-beam
    @echo ""
    cd {{build_test_path}} && erl -noshell \
        -pa _build/default/lib/*/ebin \
        -eval 'test_runner:main(["_build/default/lib/fable_typedjson_test/ebin"])' \
        -s init stop

# Transpile tests to Erlang and compile with rebar3
build-test-beam:
    dotnet build test
    {{fable}} test --exclude Fable.Core --lang beam --outDir {{build_test_path}}
    cp test/test_runner.erl {{build_test_path}}/src/
    cp test/rebar.config {{build_test_path}}/rebar.config
    cd {{build_test_path}} && rebar3 compile

# Run the full F# test suite as Python via pytest.
test-python: build-test-python
    uv run pytest -q build/python_test

# Transpile the test project to Python. FableTarget=python is read by the
# test fsproj's ProjectReference Condition (so it picks the Python shim),
# and `--define PYTHON` activates the matching `#if PYTHON` blocks in F#.
# Fable's CLI doesn't forward MSBuild properties, so we pass via env.
build-test-python:
    FableTarget=python dotnet build test
    FableTarget=python {{fable}} test --define PYTHON --exclude Fable.Core --lang python --outDir build/python_test
    cp test_python/conftest.py build/python_test/conftest.py
