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

# Build all backend shims (BEAM via Fable + rebar3, Python via Fable, JS via Fable)
build: clean build-beam build-python build-js

# Transpile core + BEAM shim to Erlang and compile with rebar3
build-beam:
    {{fable}} src/Fable.TypedJson --exclude Fable.Core --lang beam --outDir apps/fable_typed_json --noCache
    {{fable}} src/Fable.TypedJson.Beam --exclude Fable.Core --lang beam --outDir apps/fable_typed_json_beam --noCache
    rebar3 compile

# Transpile core + Python shim to Python (no further compile step needed)
build-python:
    {{fable}} src/Fable.TypedJson.Python --exclude Fable.Core --lang python --outDir build/python --noCache

# Transpile core + JS shim to JavaScript (no further compile step needed)
build-js:
    {{fable}} src/Fable.TypedJson.JS --exclude Fable.Core --lang javascript --outDir build/js --noCache

# Type check via dotnet build
check:
    dotnet build src/Fable.TypedJson
    dotnet build src/Fable.TypedJson.Beam
    dotnet build src/Fable.TypedJson.Python
    dotnet build src/Fable.TypedJson.JS

# Format source files
format:
    dotnet fantomas src/ test/

# Check formatting without modifying — used in CI to fail PRs with bad formatting
format-check:
    dotnet fantomas src/ test/ --check

# Setup tooling — restore .NET tools (no NuGet restore yet)
setup:
    dotnet tool restore

# Setup tooling + restore Paket deps + sync uv venv
restore: setup
    dotnet paket install
    uv sync

# Build and check
all: check build

# --- Release ---

# Build NuGet packages with versions extracted from each package's CHANGELOG.md
pack:
    #!/usr/bin/env bash
    set -euo pipefail
    get_version() { grep -m1 '^## ' "$1" | sed 's/^## \([^ ]*\).*/\1/'; }
    CORE_VERSION=$(get_version src/Fable.TypedJson/CHANGELOG.md)
    BEAM_VERSION=$(get_version src/Fable.TypedJson.Beam/CHANGELOG.md)
    PYTHON_VERSION=$(get_version src/Fable.TypedJson.Python/CHANGELOG.md)
    JS_VERSION=$(get_version src/Fable.TypedJson.JS/CHANGELOG.md)
    rm -rf ./nupkgs
    dotnet pack src/Fable.TypedJson        -c Release -o ./nupkgs -p:PackageVersion=$CORE_VERSION   -p:InformationalVersion=$CORE_VERSION
    dotnet pack src/Fable.TypedJson.Beam   -c Release -o ./nupkgs -p:PackageVersion=$BEAM_VERSION   -p:InformationalVersion=$BEAM_VERSION
    dotnet pack src/Fable.TypedJson.Python -c Release -o ./nupkgs -p:PackageVersion=$PYTHON_VERSION -p:InformationalVersion=$PYTHON_VERSION
    dotnet pack src/Fable.TypedJson.JS     -c Release -o ./nupkgs -p:PackageVersion=$JS_VERSION     -p:InformationalVersion=$JS_VERSION

# Pack and push every package to NuGet (CI-only — needs $NUGET_KEY)
release: pack
    dotnet nuget push './nupkgs/*.nupkg' -s https://api.nuget.org/v3/index.json -k $NUGET_KEY --skip-duplicate

# Run EasyBuild.ShipIt for release management. Pass extra flags after `--`.
shipit *args:
    dotnet shipit --pre-release rc {{args}}

# --- Test ---

build_test_path := justfile_directory() / "build/tests"

# Run all backend test suites
test: test-beam test-python test-js

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

# Run the full F# test suite as JavaScript via a Node runner.
test-js: build-test-js
    node test_js/runner.mjs

# Transpile the test project to JavaScript. Mirrors `build-test-python`:
# FableTarget=js routes the test fsproj to the JS shim, and `--define JS`
# activates the matching `#if JS` blocks in F#.
build-test-js:
    FableTarget=js dotnet build test
    FableTarget=js {{fable}} test --define JS --exclude Fable.Core --lang javascript --outDir build/js_test
