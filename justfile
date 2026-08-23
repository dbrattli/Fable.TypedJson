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

# Guard: every test module is registered in test/Main.fs.
#
# Quill runs exactly the list Main.fs hands it, so a module that compiles but is
# not listed there is silently not run — the old BEAM runner's "discovered zero
# modules is a failure" check went away with the Scriptorium migration, and this
# recipe replaces it. Compares the `module Fable.TypedJson.Tests.X` declaration
# in each test/Test*.fs against the `X.tests` entries in Main.fs.
check-test-registry:
    #!/usr/bin/env bash
    set -euo pipefail
    # LC_ALL=C pins glob and sort order — under a UTF-8 collation Testing.fs sorts
    # mid-list instead of last, which previously masked a failure in this loop.
    export LC_ALL=C
    declared=""
    for f in test/Test*.fs; do
        # Testing.fs is the shared extractor helpers, not a test module — it is
        # caught by the glob but declares `module Fable.TypedJson.Testing`.
        [ "$f" = "test/Testing.fs" ] && continue
        name=$(grep -oE '^module Fable\.TypedJson\.Tests\.[A-Za-z0-9_]+' "$f" | sed 's/.*\.//') || true
        if [ -z "$name" ]; then
            echo "error: $f declares no 'module Fable.TypedJson.Tests.<Name>'" >&2
            exit 1
        fi
        declared+="$name"$'\n'
    done
    declared=$(printf '%s' "$declared" | sort)
    listed=$(grep -E '^[[:space:]]+[A-Za-z0-9_]+\.tests[[:space:]]*$' test/Main.fs \
        | sed -E 's/[[:space:]]//g; s/\.tests$//' | sort)
    if [ "$declared" != "$listed" ]; then
        echo "error: test modules are out of sync with test/Main.fs" >&2
        diff <(echo "$declared") <(echo "$listed") \
            | sed -n 's/^< /  not run — add to Main.fs: /p; s/^> /  listed in Main.fs but no module declares it: /p' >&2
        exit 1
    fi
    echo "test registry OK — $(echo "$declared" | wc -l | tr -d ' ') modules registered"

# Type check via dotnet build
check: check-test-registry
    dotnet build src/Fable.TypedJson
    dotnet build src/Fable.TypedJson.Beam
    dotnet build src/Fable.TypedJson.Python
    dotnet build src/Fable.TypedJson.JS
    dotnet build src/Fable.TypedJson.DotNet

# Format source files
format:
    dotnet fantomas src/ test/

# Check formatting without modifying — used in CI to fail PRs with bad formatting
format-check:
    dotnet fantomas src/ test/ --check

# Setup tooling — restore .NET tools (no NuGet restore yet)
setup:
    dotnet tool restore

# Setup tooling + restore Paket deps (no uv — see `restore` for the full set)
restore-net: setup
    dotnet paket install

# Setup tooling + restore Paket deps + sync uv venv (Python target only)
restore: restore-net
    uv sync

# Build and check
all: check build

# --- Release ---

# Build the package family at the single version in its shared changelog.
#
# decision: Fable.TypedJson and its adapters release as one versioned unit because
# an adapter package contains a compiler-visible reference to the core package.
pack:
    #!/usr/bin/env bash
    set -euo pipefail
    VERSION=$(grep -m1 '^## ' src/Fable.TypedJson/CHANGELOG.md | sed 's/^## \([^ ]*\).*/\1/')
    rm -rf ./nupkgs
    dotnet pack src/Fable.TypedJson        -c Release -o ./nupkgs -p:PackageVersion=$VERSION -p:InformationalVersion=$VERSION
    dotnet pack src/Fable.TypedJson.Beam   -c Release -o ./nupkgs -p:PackageVersion=$VERSION -p:InformationalVersion=$VERSION
    dotnet pack src/Fable.TypedJson.Python -c Release -o ./nupkgs -p:PackageVersion=$VERSION -p:InformationalVersion=$VERSION
    dotnet pack src/Fable.TypedJson.JS     -c Release -o ./nupkgs -p:PackageVersion=$VERSION -p:InformationalVersion=$VERSION
    dotnet pack src/Fable.TypedJson.DotNet -c Release -o ./nupkgs -p:PackageVersion=$VERSION -p:InformationalVersion=$VERSION

# Pack and push the core before its adapters (CI-only — needs $NUGET_KEY).
release: pack
    #!/usr/bin/env bash
    set -euo pipefail
    VERSION=$(grep -m1 '^## ' src/Fable.TypedJson/CHANGELOG.md | sed 's/^## \([^ ]*\).*/\1/')
    dotnet nuget push "./nupkgs/Fable.TypedJson.$VERSION.nupkg" -s https://api.nuget.org/v3/index.json -k $NUGET_KEY --skip-duplicate
    for package in Fable.TypedJson.Beam Fable.TypedJson.Python Fable.TypedJson.JS Fable.TypedJson.DotNet; do
        dotnet nuget push "./nupkgs/$package.$VERSION.nupkg" -s https://api.nuget.org/v3/index.json -k $NUGET_KEY --skip-duplicate
    done

# Run EasyBuild.ShipIt for release management. Pass extra flags after `--`.
shipit *args:
    dotnet shipit {{args}}

# --- Benchmarks ---

# Run the .NET benchmark suite (BenchmarkDotNet). Not a competitive comparison —
# it exists to catch pathologies against a known-good baseline (see Main.fs).
# Pass BenchmarkDotNet flags directly, e.g. `just bench --job short --filter '*Decode*'`.
#
# --artifacts is pinned because `dotnet run --project` leaves the working directory
# at the repo root, so BDN would otherwise scatter output wherever it was invoked
# from. The default filter is quoted because just splices {{args}} into the shell
# line verbatim — a bare `*` glob-expands to repo filenames before BDN sees it.
bench *args='--filter "*"':
    dotnet run -c Release --project benchmarks/dotnet/Fable.TypedJson.DotNet.Benchmark.fsproj \
        -- --artifacts benchmarks/dotnet/BenchmarkDotNet.Artifacts {{args}}

# --- Test ---

build_test_path := justfile_directory() / "build/tests"

# Run all backend test suites
test: test-beam test-python test-js test-dotnet

# Build and run Fable.TypedJson tests on BEAM. Quill runs the suite
# synchronously and calls `halt/1` with the exit code, so a failing test
# makes `erl` return non-zero. Fable emits a `main.erl` shim that dispatches
# to [<EntryPoint>].
test-beam: build-test-beam
    @echo ""
    cd {{build_test_path}} && erl -noshell \
        -pa _build/default/lib/*/ebin \
        -eval 'main:main([])'

# Transpile tests to Erlang and compile with rebar3
build-test-beam:
    dotnet build test/Fable.TypedJson.Test.Beam.fsproj
    {{fable}} test/Fable.TypedJson.Test.Beam.fsproj --exclude Fable.Core --lang beam --outDir {{build_test_path}}
    cp test/rebar.config {{build_test_path}}/rebar.config
    cd {{build_test_path}} && rebar3 compile

# Run the full F# test suite as Python. Quill is the runner — no pytest.
test-python: build-test-python
    uv run python build/python_test/main.py

# Transpile the Python test project. It references only the Python shim and
# Fable.Python, so nothing from the other backends lands in the output.
build-test-python:
    dotnet build test/Fable.TypedJson.Test.Python.fsproj
    {{fable}} test/Fable.TypedJson.Test.Python.fsproj --define PYTHON --exclude Fable.Core --lang python --outDir build/python_test

# Run the full F# test suite as JavaScript under Node. Node cannot block, so
# Quill chains `process.exit` onto the resolved promise itself.
test-js: build-test-js
    echo '{"type":"module"}' > build/js_test/package.json
    node build/js_test/Main.js

# Transpile the JavaScript test project. Mirrors `build-test-python`; the JS
# shim needs only Fable.Core, so no bindings package is referenced at all.
build-test-js:
    dotnet build test/Fable.TypedJson.Test.JS.fsproj
    {{fable}} test/Fable.TypedJson.Test.JS.fsproj --define JS --exclude Fable.Core --lang javascript --outDir build/js_test

# Run the full F# test suite natively on the .NET CLR. No Fable transpile —
# the .NET test project compiles directly to IL, drops FABLE_COMPILER and
# references the .NET shim. Quill's runner in Main.fs drives the same suite
# the Fable targets run.
test-dotnet:
    dotnet run --project test/Fable.TypedJson.Test.DotNet.fsproj
