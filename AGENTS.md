# Repository Guidelines

## Project Structure & Module Organization

`src/Fable.TypedJson/` contains the backend-neutral F# core. Target adapters live in sibling projects: `Fable.TypedJson.Beam`, `.Python`, `.JS`, and `.DotNet`. Keep backend-specific JSON operations behind `IJsonBackend`; avoid introducing target dependencies into the core.

Tests are shared across all four runtimes in `test/`. Each `Test*.fs` module is compiled through target-specific `.fsproj` files and registered in `test/Main.fs`. .NET benchmarks live in `benchmarks/dotnet/`. Generated output goes to `apps/`, `build/`, or `_build/` and should not be edited.

## Build, Test, and Development Commands

Use the repository `justfile` as the primary interface:

- `just restore` restores .NET/Paket tools and the Python `uv` environment; use `just restore-net` when Python is unnecessary.
- `just check` type-checks every source project and verifies the test registry.
- `just build` transpiles and builds the BEAM, Python, and JavaScript targets.
- `just test` runs the shared suite on all targets. Use `just test-dotnet`, `test-js`, `test-python`, or `test-beam` for one runtime.
- `just format` applies Fantomas; `just format-check` performs the CI formatting check.
- `just bench --job short` runs the BenchmarkDotNet suite.

Run `just setup` after cloning if only tool installation is needed.

## Coding Style & Naming Conventions

Follow Fantomas and `.editorconfig`: four-space F# indentation, Stroustrup-style multiline brackets, and a 140-character line limit. Use PascalCase for F# types, modules, union cases, and public members; use camelCase for values and functions. Keep modules aligned with filenames and preserve `.fsproj` compile order.

This repository uses Agent Decision Comments (ADC) v0.1.1. Read `AGENT_DECISION_COMMENTS.md` before modifying code and collect all active comments in the affected scope. Preserve or explicitly update existing directives, and add concise `decision:`, `invariant:`, `assumption:`, or `tradeoff:` comments for non-obvious engineering rationale. Upstream releases are published at <https://github.com/dbrattli/adc/releases>.

## Testing Guidelines

Tests use Scriptorium Quill with Nib assertions. Name files `TestFeature.fs`, declare `module Fable.TypedJson.Tests.Feature`, expose `let tests`, add the file to `test/Tests.props`, and register `Feature.tests` in `test/Main.fs`. Run `just check-test-registry` to catch omissions. Use `ftest`/`ftestList` temporarily for focused runs and remove focus markers before committing.

## Commit & Pull Request Guidelines

History and CI follow Conventional Commits: `feat:`, `fix:`, `test:`, `docs:`, `refactor:`, `chore:`, and related types. Keep subjects imperative and scoped to one change. Pull requests should explain motivation and behavior, link relevant issues, note backend differences, and include the commands run. Ensure `just check`, `just format-check`, and applicable target tests pass; add screenshots only for visual documentation changes.
