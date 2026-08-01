# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See `GRIT.md` for the annotation convention used in this codebase. New/changed code must carry GRIT directives (`adr:`, `invariant:`, `principle:`, `assumption:`, `tradeoff:`) co-located with the code they describe; existing directives are binding constraints, not suggestions.

## Build / Test / Format

All workflows go through `just` (see `justfile`). Builds run Fable to transpile F# to Erlang, then `rebar3 compile`.

- `just restore` — restore .NET tools (Fable, Paket, Fantomas) and Paket deps. Run once after clone.
- `just build` — clean, then `build-beam` (Fable + rebar3 to `apps/`), `build-python` (Fable to `build/python/`), and `build-js` (Fable to `build/js/`).
- `just check` — `dotnet build` of every src project (type-check only, no Fable).
- `just test` — runs the same suite on all four targets: `test-beam`, `test-python`, `test-js`, `test-dotnet`.
- `just build-test-beam` / `build-test-python` / `build-test-js` — transpile tests to a single backend; useful when debugging compile failures.
- `just format` — Fantomas over `src/` and `test/`.
- `just dev=true build` — use a local Fable repo at `../Fable` instead of the `dotnet fable` tool. Same flag works for `test`.

There is no single-test filter on the command line. Narrow scope with Quill's marks instead: `ftest` / `ftestList` focuses (everything else is skipped), `xtest` / `xtestList` marks pending.

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
- **What `PropertyInfo.Name` reports is not uniform across targets, and has changed over time.** .NET and JS always gave the F# field name (`AirTemperature`); BEAM gave snake_case until Fable 5.8.1 ([Fable#4766](https://github.com/fable-compiler/Fable/pull/4766)); Python gives snake_case (`air_temperature`) until Fable 5.14.0 ([Fable#4852](https://github.com/fable-compiler/Fable/pull/4852)). Never assume a spelling — pin any new key derivation to `Casing.toCanonicalPascal` and it stops mattering.
- `Casing.fs` (compiled before `Schema.fs`) holds that pivot: normalize to PascalCase, then emit. Both key paths go through it — `Schema.lowerFirstTransform` (backing `dump` / `validateJson` / `validateMap`) and `Json.applyCaseRule` (backing `CaseRules`). Keep the two-step path when adding rules; it is what makes a JSON key a function of the F# field name alone, on every target and Fable version. The reverse is lossy for adjacent capitals (`HTTPStatus` → `httpstatus` → `Httpstatus`), which only Fable#4852 truly fixes.

### Adding a new backend

Implement `IJsonBackend` in a new `Fable.TypedJson.<Target>` project, plus a `<Target>.Json` convenience module that pre-applies the backend (mirror `Fable.TypedJson.Beam.Json`). The core library should not need to change.

### Fable-specific constraints

- Attribute reflection at runtime is out — Fable erases attributes in generated code. Design APIs around `IJsonCodec`/registries and combinator pipelines, not attributes on types.
- `Fable.AST` `Field` interface lacks `Attributes`, so plugin work cannot read field-level attributes either. (See `Feliz.CompilerPlugins` for canonical plugin examples if plugin work becomes necessary.)
- This library is intended to run on multiple Fable backends (BEAM, Python, JS today; .NET planned). Keep the core format-agnostic; backend-specific code lives only behind `IJsonBackend`.

## Tests

Tests live in `test/` and are compiled to all four targets from one source set. Each target has its own project (`Fable.TypedJson.Test.Beam.fsproj`, `.Python`, `.JS`, `.DotNet`) sharing the compile order in `Tests.props`, and each carries its own `<project>.fsproj.paket.references` naming only that backend's bindings — so a target's build matches what a consumer of that package actually gets. The `#if PYTHON | JS | DOTNET` header in each file selects the matching shim.

Tests are written with [Scriptorium](https://github.com/fable-hub/Scriptorium) — Quill for the test DSL and runner, Nib for assertions — both of which compile to every target. Each module groups its tests into `testList`s named after the file's `// ====` section banners and exposes a single `let tests`; `Main.fs` is the one `[<EntryPoint>]`, handing that list to Quill's `runTests`, which returns the process exit code on every target. `Testing.fs` keeps only the backend-portable `getString` / `getInt` / … extractors.

Known per-target divergences are marked with Quill's `skipIfJavaScript` / `skipIfDotNet` configurers colocated with the test, each carrying a comment explaining the gap. Quill has no skip-reason field, so the comment is the record.

New test modules need a `module Fable.TypedJson.Tests.X` matching the file, must be added to `Tests.props` in compile order, and their `tests` value must be added to the list in `Main.fs` — a module that is not listed there is silently not run.
