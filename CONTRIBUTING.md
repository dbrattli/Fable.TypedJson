# Contributing

[README](README.md) · [Architecture](docs/architecture.md)

Use the repository `justfile` for setup, builds, checks, tests, and benchmarks.

## Prerequisites

- .NET SDK 10; the .NET adapter and test projects target `net10.0`.
- `just` for repository tasks.
- Erlang/OTP and `rebar3` for BEAM; CI uses OTP 27.
- `uv` for Python dependencies and execution.
- Node.js for JavaScript tests; CI uses Node.js 20.

## Setup and workflow

```sh
just restore        # .NET tools, Paket dependencies, and the uv environment
just check          # test registry, benchmark configuration, and all source projects
just build          # transpile and build BEAM, Python, and JavaScript
just test           # run the shared suite on all four runtimes
just format         # apply Fantomas
just format-check   # check formatting without modifying files
```

`just setup` restores .NET tools only. `just restore-net` restores tools and
Paket dependencies when Python is unnecessary. `just build` cleans generated
output first.

Use `just build-beam`, `just build-python`, or `just build-js` for a single
target. Tests have `just test-beam`, `just test-python`, `just test-js`, and
`just test-dotnet` variants, which build what they need. Paket dependencies
are split into `Main`, `Beam`, `Python`, `JS`, and `DotNet` groups.

For .NET benchmarks, run `just bench --job short`. See the
[benchmark harness guide](benchmarks/cli/README.md) for shared CodSpeed workloads
and measurement instructions.

## Code organization

The backend-neutral core lives in `src/Fable.TypedJson/`. Sibling adapter
projects implement native JSON operations behind `IJsonBackend`. Keep the shared
codec workflow readable and place specialized traversal, caching, representation
helpers, bindings, and compiler workarounds in the owning project's
`Optimizations/` folder.

Keep compiler conditionals inside those helpers and select capabilities at
codec construction. Substantial target-specific algorithms belong in Erlang,
JavaScript, or Python, with thin F# bindings. Include native assets in both local
builds and the Fable package so consumers need no repository copy recipes.
See [Architecture](docs/architecture.md) for the contracts.

Generated files belong under `apps/`, `build/`, or `_build/`; edit their source
inputs instead. Follow `.editorconfig` and Fantomas, including four-space F#
indentation and the 140-character line limit. Preserve `.fsproj` compile order.

## Tests

Shared F# test sources compile for BEAM, Python, JavaScript, and .NET. Tests use
[Scriptorium](https://github.com/fable-hub/Scriptorium): Quill supplies the test
DSL and runner, and Nib supplies assertions. Quill exits non-zero on failure.

When adding a test module:

1. Name the file `test/TestFeature.fs`.
2. Declare `module Fable.TypedJson.Tests.Feature` and expose `let tests`.
3. Include it in `test/Tests.props` in the correct compile order.
4. Register `Feature.tests` in `test/Main.fs`.
5. Run `just check-test-registry` and the applicable target tests.

Remove temporary `ftest` / `ftestList` focus markers before committing.
Document target-specific gaps beside skip configuration so skipped tests remain
visible in the results.

## Review and pull requests

Read [AGENTS.md](AGENTS.md) and the repository's
[Agent Decision Comments convention](AGENT_DECISION_COMMENTS.md) before changing
code. Collect active comments in the affected scope and preserve or explicitly
update them. Explain non-obvious decisions close to the implementation.

Use Conventional Commits such as `feat:`, `fix:`, `test:`, `docs:`, or
`refactor:`. Keep each change focused. Pull requests should explain motivation
and resulting behavior, link relevant issues, state backend differences, and
list the commands run. Run `just check`, `just format-check`, and applicable
target tests. For documentation examples, verify API signatures and stated
behavior against the current implementation.
