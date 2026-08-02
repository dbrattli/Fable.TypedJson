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

**The library is a staged compiler.** Stage 1 (codec construction) walks `typeof<'T>` once and emits a tree of closures; stage 2 (per decode/encode call) runs them. No `System.Type`, no `FullName` comparison and no `FSharpType` call survives into stage 2, at any depth. Build a codec once and reuse it — that is where the design puts the cost.

```
Json      (public API: CaseRules + aliases + TypedJson<'T> + combinators)
   │       TypedJson.fs
   ▼
Plan      (THE type-walker: one walk emits decode + encode + JSON Schema)
   │       Plan.fs      — forType, forTypeFromLookup
   ▼
Schema    (vocabulary: JsonValue, FieldError, IJsonCodec, registry, reflection helpers)
   │       Schema.fs, Primitives.fs (the one coercion table), Casing.fs (the PascalCase pivot)
   ▼
IJsonBackend (per-target shim: NewMap/Get/Put/Parse/Stringify + type tests + accessors)
          Backend.fs (interface)
          Fable.TypedJson.Beam/Backend.fs (concrete BEAM impl over jsx + maps)
```

- **There is exactly one walker.** `Plan.forType` returns a `Plan` carrying `Decode`, `Encode` and `Schema` for the same node. Do not add a second traversal of the type tree — the three used to be separate and drifted: the schema emitter had no union branch at all, and encode silently dropped payloads decode rejected. Anything that needs to know a type's shape reads it off the plan.
- `JsonValue` is `[<Struct>]`, not `[<Erase>]`. It exists only at the `IJsonCodec` boundary — `toJsonValue` / `fromJsonValue` are the only constructors. The hot path never builds one; it goes through `IJsonBackend.IsX` / `AsX`.
- Type resolution dispatches on `FullName` strings, not `System.Type` identity, so it is portable across Fable backends — but this happens **once per codec**, not per value. Cross-type coercion (`"42"` → `int`) is intentional: LLM tool calls deliver everything as strings.
- `Primitives.fs` is the single coercion table, compiled ahead of both `Schema.fs` and `Codec.fs` so neither can fork it. `Schema.coerce` and `Codec.float` once had independent copies and disagreed on locale handling.
- `IJsonCodec<'T>` + `CodecRegistry` is the validators-as-types path, and it drives **both** directions — a registered codec's `Encode` is what stops a wrapper DU being mistaken for a tagged union. Combinators in `Codec.fs` (`gt`, `le`, `pattern`, `refine`, `map`, …) decorate a base codec with extra `Decode`-side validation.
- `auto<'T>` must be `inline` so Fable resolves `typeof<'T>` at the call site on each backend. Keep the body to a single call into a non-inline function taking `System.Type` — anything an inline body touches must be public at consumer call sites, which is what inflated the exported surface.
- Unsupported shapes (multi-field union cases, non-record payloads) are rejected at **codec construction**, not per value.
- Recursive types (`Tree = { Children: Tree list }`) would make an eager walk non-terminating. `Plan`'s `Building` path defers the sub-walk on re-entry rather than tying a knot through a mutable `ref` — captured refs are the one construct whose Fable BEAM lowering is unverified.
- **What `PropertyInfo.Name` reports is not uniform across targets, and has changed over time.** .NET and JS always gave the F# field name (`AirTemperature`); BEAM gave snake_case until Fable 5.8.1 ([Fable#4766](https://github.com/fable-compiler/Fable/pull/4766)); Python gives snake_case (`air_temperature`) until Fable 5.14.0 ([Fable#4852](https://github.com/fable-compiler/Fable/pull/4852)). Never assume a spelling — pin any new key derivation to `Casing.toCanonicalPascal` and it stops mattering.
- `Casing.fs` (compiled before `Schema.fs`) holds that pivot: normalize to PascalCase, then emit. Every key now goes through `Json.applyCaseRule` — `dump` / `validateJson` / `validateMap` use `Json.camelCaseKey`, which is the same function partially applied, so the shortcut entry points and a default-configured codec cannot disagree. Keep the two-step path when adding rules; it is what makes a JSON key a function of the F# field name alone, on every target and Fable version. The reverse is lossy for adjacent capitals (`HTTPStatus` → `httpstatus` → `Httpstatus`), which only Fable#4852 truly fixes.

### Layout

- `src/Fable.TypedJson/` — backend-agnostic core
- `src/Fable.TypedJson.Beam/` — BEAM backend shim (jsx + maps)
- `src/Fable.TypedJson.Python/` — Python backend shim (`json`)
- `src/Fable.TypedJson.JS/` — JavaScript backend shim (`JSON.parse` / `JSON.stringify`)
- `src/Fable.TypedJson.DotNet/` — .NET backend shim (`System.Text.Json`, runs natively on the CLR, `net10.0`; the others are `netstandard2.0`)
- `test/` — one F# test source set compiled to all four targets
- `benchmarks/` — BenchmarkDotNet suite (`just bench`), .NET only

Core modules, in compile order:

```text
Fable.TypedJson (core, no backend deps)
├── Backend        IJsonBackend interface — map operations, IsX type tests, AsX accessors
├── Casing         the canonical-PascalCase pivot every key derivation goes through
├── Primitives     the one JSON <-> F# primitive coercion table
├── Schema         vocabulary: JsonValue, FieldError, IJsonCodec, registry, reflection helpers
├── Codec          IJsonCodec primitives + pipeline combinators (gt, lt, minLength, ...)
├── Refined        bundled refined types (NonEmptyString, PositiveInt, Email, Url, Uuid)
├── Plan           the type-walker (internal) — one walk emits decode + encode + JSON Schema
├── TypedJson      public API: CaseRules, aliases, TypedJson<'T>, combinators
└── JsonSchemaGen  renders a plan's schema to a JSON document
```

### Adding a new backend

Implement `IJsonBackend` in a new `Fable.TypedJson.<Target>` project, plus a `<Target>.Json` convenience module that pre-applies the backend (mirror `Fable.TypedJson.Beam.Json`). The core library should not need to change.

`IJsonBackend` exposes three groups of operations:

- **Map ops:** `NewMap`, `Get`, `Put`, `ContainsKey`, `ParseRaw`, `Stringify`, `Null`.
- **Type tests:** `IsString` / `IsInt` / `IsFloat` / `IsBool` / `IsNull` / `IsArray` / `IsMap`.
- **Typed accessors:** `AsString` / `AsInt` / `AsFloat` / `AsBool` — paired with the type tests so the walker can dispatch directly on the backend's native shape (Erlang binary, Python `str`, JS `string`, or a CLR `JsonValue` case) without going through a shared `[<Erase>]` DU pattern. The `JsonValue` DU is reserved for the user-facing `IJsonCodec` API; the internal hot path operates entirely on `obj` + the backend's accessor pair.

### Fable-specific constraints

- Attribute reflection at runtime is out — Fable erases attributes in generated code. Design APIs around `IJsonCodec`/registries and combinator pipelines, not attributes on types.
- `Fable.AST` `Field` interface lacks `Attributes`, so plugin work cannot read field-level attributes either. (See `Feliz.CompilerPlugins` for canonical plugin examples if plugin work becomes necessary.)
- This library is intended to run on multiple Fable backends (BEAM, Python, JS today; .NET planned). Keep the core format-agnostic; backend-specific code lives only behind `IJsonBackend`.

## Tests

Tests live in `test/` and are compiled to all four targets from one source set. Each target has its own project (`Fable.TypedJson.Test.Beam.fsproj`, `.Python`, `.JS`, `.DotNet`) sharing the compile order in `Tests.props`, and each carries its own `<project>.fsproj.paket.references` naming only that backend's bindings — so a target's build matches what a consumer of that package actually gets. The `#if PYTHON | JS | DOTNET` header in each file selects the matching shim.

Tests are written with [Scriptorium](https://github.com/fable-hub/Scriptorium) — Quill for the test DSL and runner, Nib for assertions — both of which compile to every target. Each module groups its tests into `testList`s named after the file's `// ====` section banners and exposes a single `let tests`; `Main.fs` is the one `[<EntryPoint>]`, handing that list to Quill's `runTests`, which returns the process exit code on every target. `Testing.fs` keeps only the backend-portable `getString` / `getInt` / … extractors.

Known per-target divergences are marked with Quill's `skipIfJavaScript` / `skipIfDotNet` configurers colocated with the test, each carrying a comment explaining the gap. Quill has no skip-reason field, so the comment is the record.

New test modules need a `module Fable.TypedJson.Tests.X` matching the file, must be added to `Tests.props` in compile order, and their `tests` value must be added to the list in `Main.fs` — a module that is not listed there is silently not run.
