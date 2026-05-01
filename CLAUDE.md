# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See `GRIT.md` for the annotation convention used in this codebase. New/changed code must carry GRIT directives (`adr:`, `invariant:`, `principle:`, `assumption:`, `tradeoff:`) co-located with the code they describe; existing directives are binding constraints, not suggestions.

## Build / Test / Format

All workflows go through `just` (see `justfile`). Builds run Fable to transpile F# to Erlang, then `rebar3 compile`.

- `just restore` — restore .NET tools (Fable, Paket, Fantomas) and Paket deps. Run once after clone.
- `just build` — clean, transpile `src/Fable.TypedJson` and `src/Fable.TypedJson.Beam` to `apps/`, run `rebar3 compile`.
- `just check` — `dotnet build` of both src projects (type-check only, no Fable).
- `just test` — transpile tests to Erlang in `build/tests/`, compile, run BEAM test suite via `test_runner.erl`.
- `just build-test` — only the test build; useful when debugging compile failures.
- `just format` — Fantomas over `src/` and `test/`.
- `just dev=true build` — use a local Fable repo at `../Fable` instead of the `dotnet fable` tool. Same flag works for `test`.

There is no single-test runner; `test_runner.erl` discovers all `test_*` modules from compiled `.beam` files and runs every `test_*/0` export. Narrow scope by temporarily renaming or removing `[<Fact>]` attributes.

## Architecture

Three layers, each in its own module, designed so the core is backend-agnostic and a per-Fable-target shim provides the JSON map plumbing.

```
Schema  (format-agnostic validation, reflection over record fields)
   │      Schema.fs   — auto<'T>, coerce, JsonValue, IJsonCodec, codecRegistry
   ▼
TypedJson (JSON layer: CaseRules + Encode + decode/encode pair)
   │      TypedJson.fs
   ▼
IJsonBackend (per-target shim: NewMap/Get/Put/Parse/Stringify)
          Backend.fs (interface)
          Fable.TypedJson.Beam/Backend.fs (concrete BEAM impl over jsx + maps)
```

- `Schema<'T>` is `(string -> JsonValue option) -> Result<'T, FieldError list>` — a function from a lookup to an accumulated-errors result. Adapters (`stringMapAdapter`, `jsonMapAdapter`) bridge concrete sources to that lookup. Errors accumulate, not fail-fast.
- `JsonValue` is an `[<Erase>]` DU. At runtime `JString s` IS the underlying BEAM binary; pattern matching compiles to Erlang type guards. Treat it as zero-cost — do not introduce boxed wrappers.
- Coercion is dispatched on `PropertyType.FullName` strings (e.g., `"System.Int32"`), not `System.Type` identity, so it is portable across Fable backends. Cross-type coercion (`JString "42"` → `int`) is intentional — LLM tool calls deliver everything as strings.
- `IJsonCodec<'T>` + `codecRegistry` is the validators-as-types path. Users call `Codec.register<'T> codec` at module init; `Schema.coerce` falls through to the registry when the target isn't a known primitive. Combinators in `Codec.fs` (`gt`, `le`, `pattern`, `refine`, `map`, …) decorate a base codec with extra `Decode`-side validation.
- `auto<'T>` must be `inline` so Fable resolves `typeof<'T>` at the call site on each backend.
- Field reflection on BEAM yields **snake_case** names (Fable lowercases F# record field names). `CaseRules` (in `TypedJson.fs`) transforms snake_case → caller's chosen JSON casing on encode, and the inverse on decode. `applyCaseRule` first reverses to PascalCase via `fromSnakeCase`, then re-emits — keep this two-step path when adding new rules.

### Adding a new backend

Implement `IJsonBackend` in a new `Fable.TypedJson.<Target>` project, plus a `<Target>.Json` convenience module that pre-applies the backend (mirror `Fable.TypedJson.Beam.Json`). The core library should not need to change.

### Fable-specific constraints

- Attribute reflection at runtime is out — Fable erases attributes in generated code. Design APIs around `IJsonCodec`/registries and combinator pipelines, not attributes on types.
- `Fable.AST` `Field` interface lacks `Attributes`, so plugin work cannot read field-level attributes either. (See `Feliz.CompilerPlugins` for canonical plugin examples if plugin work becomes necessary.)
- This library is intended to run on multiple Fable backends (BEAM today; JS, Python, .NET planned). Keep the core format-agnostic; backend-specific code lives only behind `IJsonBackend`.

## Tests

Tests live in `test/` and are transpiled to Erlang. They use `Fable.Core.Testing.Assert` wrapped by `Testing.fs` (`Fact`, `equal`, `notEqual`). `Main.fs` is empty under `FABLE_COMPILER` — the Erlang `test_runner` is the entry point. New test modules need a `module Fable.TypedJson.Tests.X` matching the file, and they must be added to `Fable.TypedJson.Test.fsproj` in compile order.
