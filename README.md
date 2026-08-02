# Fable.TypedJson

[![Build and Test](https://github.com/dbrattli/Fable.TypedJson/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/dbrattli/Fable.TypedJson/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/Fable.TypedJson.svg)](https://www.nuget.org/packages/Fable.TypedJson)

Pydantic-flavored JSON validation and serialization for F# records, designed for Fable's multi-backend output. **BEAM (Erlang), Python, JavaScript, and .NET all work today**, sharing a single `IJsonBackend` abstraction.

The headline idea: **validation lives with the type**. Define a wrapper DU, ship a `JsonCodec` static member on it, and `auto<'T>()` discovers it and dispatches through it — the F# answer to Pydantic's "custom types with embedded validators."

## Quick example

```fsharp
open Fable.TypedJson             // brings the module name `Codec` into scope
open Fable.TypedJson.Schema      // IJsonCodec, emptyRegistry, register, formatErrors
open Fable.TypedJson.Refined     // NonEmptyString, PositiveInt, Email, Url, Uuid
open Fable.TypedJson.Json        // CaseRules, withModel, alias

// Pick the backend convenience module for your target.
open Fable.TypedJson.Beam.Json   // or .Python.Json / .JS.Json (Fable targets) / .DotNet.Json (native CLR)

// 1. Define a wrapper type. The pipeline composes validators the same way
//    Pydantic composes  Annotated[int, Field(gt=0, le=14)].
type Days =
    | Days of int

    static member JsonCodec: IJsonCodec<Days> =
        Codec.int |> Codec.gt 0 |> Codec.le 14
        |> Codec.map Days (fun (Days n) -> n)

// 2. Use it like any field type. Optional fields handle missing-as-None natively.
type WeatherRequest = {
    Location: NonEmptyString          // bundled refined type
    Days: Days                         // user-defined validator
    Detailed: bool option
}

// 3. Build a registry once with the codecs your records use.
let codecs =
    emptyRegistry
    |> register Days.JsonCodec
    |> registerAll                      // NonEmptyString, PositiveInt, Email, Url, ...

// 4. Auto-derive the codec and decode. Errors accumulate across all fields.
//    The default JSON casing is camelCase (`LowerFirst`); chain
//    `withCaseRules CaseRules.SnakeCase` if you need snake_case keys.
let codec = autoWith<WeatherRequest> codecs

match codec.decode jsonMap with
| Ok req -> handle req
| Error errs ->
    // [{ path = "days";     message = "must be > 0" };
    //  { path = "location"; message = "must be non-empty" }]
    printfn "%s" (formatErrors errs)
```

For one-off rules where a named wrapper type would be overkill, the same combinator pipeline is the F# Annotated equivalent — `Codec.int |> Codec.gt 0 |> Codec.le 14` plays the role of `Annotated[int, Field(gt=0, le=14)]`.

## JSON Schema generation

Like Pydantic's `model_json_schema()`, a single call walks the codec tree and emits a JSON Schema document — handy for OpenAPI specs, LLM tool definitions, or runtime introspection. Constraints from the codec combinators (`minLength`, `pattern`, `gt`, ...) flow into the right schema keywords.

```fsharp
type Account = { Username: NonEmptyString; Email: Email }

let codec = autoWith<Account> codecs
let schemaJson = jsonSchemaOfCodec codecs codec
// {
//   "type": "object",
//   "title": "Account",
//   "properties": {
//     "username": { "type": "string", "minLength": 1 },
//     "email":    { "type": "string", "pattern": "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$" }
//   },
//   "required": ["username", "email"]
// }
```

`jsonSchemaOfCodec` reads the codec's configured `caseRules` and any `alias` overrides, so the schema's property names always match the JSON the codec actually accepts and produces. (For a quick schema with no aliases or codec, `jsonSchemaOf<'T> codecs caseRules` takes the case rule explicitly.)

## Aliases and cross-field rules

```fsharp
type Range = { Start: int; Until: int }

let codec =
    auto<Range> ()
    |> alias "Until" "end"          // override the JSON key for one field
    |> withModel (fun r ->          // cross-field invariant (Pydantic @model_validator)
        if r.Start <= r.Until then Ok r
        else Error [ { path = ""; message = "start must precede end" } ])
```

`alias` flows through decode lookup, encode output, **and** the JSON Schema's `properties` / `required` keys. Field names you pass to `alias` are normalized to PascalCase internally so the same call works on every backend (BEAM lowercases reflection names, Python preserves the F# spelling).

## Validating dicts and maps too

The codec is the whole API — there is no second, weaker path. Three shorthands
cover the common sources, all going through the same walker and the same
camelCase key derivation a default codec uses:

```fsharp
// Map<string, string> — LLM tool-call inputs, form fields, env vars, ...
// Every value arrives as a string; primitive coercion turns it into the
// record's declared type (see `Type coercion` below).
let toolInput : Map<string, string> =
    Map.ofList [ "location", "Oslo"; "days", "3"; "detailed", "true" ]

match validateMapWith<WeatherRequest> codecs toolInput with
| Ok req -> handle req
| Error errs -> printfn "%s" (formatErrors errs)
```

```fsharp
// Backend-native JSON map (a parsed jsx map / Python dict / ...)
let jsonMap = parseRaw """{"location":"Oslo","days":3}"""

match validateJsonWith<WeatherRequest> codecs jsonMap with
| Ok req -> handle req
| Error errs -> printfn "%s" (formatErrors errs)
```

Use the `…With` forms whenever your record has refined or custom-codec fields —
plain `validateMap` / `validateJson` use an empty registry, so a
`NonEmptyString` or `Days` field has no codec to dispatch through. `dump`
is the encode-side counterpart, producing a backend-native map instead of a
string.

All of these build a plan per call. **Build a codec once and reuse it** for
anything repeated:

```fsharp
let codec = autoWith<WeatherRequest> codecs   // module level, not per request
```

Construction resolves the entire type tree — every nested record, list element
type and union case — so the per-call path does no reflection at any depth.
That is the trade the design makes: construction is expensive, decoding is
cheap.

## Approach

- **Validators-as-types**: each field's type carries its rules. Reuse across records, compose across libraries.
- **Pipeline combinators**: `Codec.gt`, `Codec.lt`, `Codec.ge`, `Codec.le`, `Codec.minLength`, `Codec.maxLength`, `Codec.nonEmpty`, `Codec.pattern`, `Codec.refine`, `Codec.map`, `Codec.describe`. Apply to any `IJsonCodec<'T>`.
- **Reflection-driven `auto<'T>`**: walks F# record fields, recurses into nested records, handles `'T list` / `'T[]` / `'T option`, and looks up custom-typed fields via the registry. No per-record boilerplate.
- **Bundled refined types**: `NonEmptyString`, `PositiveInt`, `NonNegativeInt`, `Email`, `Url`, `Uuid` — register them all in one call (`registerAll`, after `open Fable.TypedJson.Refined`) or pick à la carte.
- **Multi-backend by design**: an `IJsonBackend` abstraction in the core, with concrete shims for BEAM (jsx), Python (`json`), JavaScript (`JSON.parse` / `JSON.stringify`), and .NET (`System.Text.Json`).
- **Errors accumulate**: a single `Error` result lists every per-field problem with a `path`, not just the first.
- **String-coerced primitives**: a JSON `"42"` decodes as `int 42`. Useful for LLM tool calls and shell input where everything arrives as strings.
- **JSON Schema generation**: `jsonSchemaOf<'T>` / `jsonSchemaOfCodec codec` emit a JSON Schema doc from the same walk that drives decode and encode, with combinator constraints (`minLength`, `pattern`, `exclusiveMinimum`, ...) folded in.
- **Cross-field validators**: `withModel (fun r -> ...)` for invariants that span fields.
- **Field aliases**: `alias "FieldName" "json_key"` overrides the JSON-key derivation per field. Reflected in decode, encode, and the generated schema.

## Tagged discriminated unions

An F# DU decodes and encodes as `{"type": "<case>", ...payload}` — the
Pydantic / OpenAPI discriminated-union convention, and the shape Anthropic's
and OpenAI's message formats use.

```fsharp
type SearchInput = { Query: string; MaxResults: int }

type Tool =
    | Search of SearchInput
    | Ping

let codec = auto<Tool> () |> withCaseRules CaseRules.SnakeCase

codec.decode (parseRaw """{"type":"search","query":"hello","max_results":5}""")
// Ok (Search { Query = "hello"; MaxResults = 5 })

codec.encode Ping
// {"type":"ping"}
```

A single record-payload case flattens its fields alongside the discriminator;
a fieldless case is just the discriminator. The case name goes through the
codec's `CaseRules`, so `ToolUse` becomes `"tool_use"` under `SnakeCase`.

Two shapes are **not** supported in v1: a case with a non-record payload
(`Circle of float`) and a case with multiple positional fields
(`At of int * int`). Both are rejected when the codec is built, not when a
document happens to select that case — a DU with a case that cannot
round-trip is a broken codec either way, and finding out at construction beats
finding out in production. Wrap the payload in a record, or register an
`IJsonCodec` for the type.

`jsonSchemaOf<Tool>` emits a `oneOf` branch per case, each pinning its
discriminator with `const`.

## Case rules

Field names from F# reflection are transformed into JSON keys via a `CaseRules` setting on the codec. The default is `LowerFirst` (camelCase) — the most common convention for modern JSON APIs. Use `withCaseRules` to switch.

```fsharp
type Reading = { AirTemperature: float; WindSpeed: float }

let reading = { AirTemperature = 22.5; WindSpeed = 3.0 }

// Default (camelCase): no extra step needed.
// After `open Fable.TypedJson.Beam.Json` the backend is already applied,
// so `auto` takes `()` — you never thread `beam` yourself.
(auto<Reading> ()).encode reading
// {"airTemperature":22.5,"windSpeed":3.0}

(auto<Reading> () |> withCaseRules CaseRules.SnakeCase).encode reading
// {"air_temperature":22.5,"wind_speed":3.0}

// Single-word fields look identical under either rule — multi-word names are
// the only place the difference shows.

// One-off override (rare — same codec, multiple JSON formats). Note this
// rebuilds the codec, so hoist `withCaseRules` if it is on a hot path:
codec.decodeWith CaseRules.SnakeCaseAllCaps map
```

The rule normalizes names through PascalCase internally so the same rule produces consistent output regardless of how the backend's reflection presents the F# name (BEAM lowercases, Python preserves):

| Rule               | Input            | Output           |
| ------------------ | ---------------- | ---------------- |
| `None`             | `MyField`        | `MyField`        |
| `LowerFirst` (default) | `MyField`    | `myField`        |
| `SnakeCase`        | `MyField`        | `my_field`       |
| `SnakeCaseAllCaps` | `MyField`        | `MY_FIELD`       |
| `KebabCase`        | `MyField`        | `my-field`       |
| `PascalCase`       | `my_field`       | `MyField`        |

## Type coercion

Built-in primitive codecs accept several source types — useful when JSON input comes from LLM tool calls or shells where everything is a string:

| Target type | Accepted sources                    |
| ----------- | ----------------------------------- |
| `string`    | string, int, float, bool            |
| `int`       | int, float, string (parseable)      |
| `int64`     | int, float, string (parseable)      |
| `float`     | float, int, string (parseable)      |
| `bool`      | bool, string (`"true"` / `"false"`) |

## Error handling

```fsharp
Error [
    { path = "days";     message = "must be > 0" }
    { path = "location"; message = "must be non-empty" }
    { path = "contact";  message = "must match pattern '^[^\s@]+@[^\s@]+\.[^\s@]+$'" }
]
```

`formatErrors` turns the list into a single human-readable string — handy for surfacing back to an LLM as a tool error, or to a user as a form-validation summary.

## How it compares

This isn't a "better than" claim — it's a fit-for-purpose claim. Pick what matches your needs.

### vs. [Thoth.Json](https://thoth-org.github.io/Thoth.Json/)

Thoth is the established F#/Fable JSON library and the closest neighbor. Both lean on F# reflection; the pivot is around what's idiomatic.

|                        | Thoth.Json                                                             | Fable.TypedJson                                                                            |
| ---------------------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| Primary style          | Manual `Decode.field "x" Decode.string` decoders; `Auto<'T>` is opt-in | Reflection-driven `auto<'T>` is the primary path                                           |
| Per-type customization | Pass "extra coders" alongside the decoder                              | Define a wrapper DU's static `JsonCodec` member; `register` once                           |
| Constraint composition | Compose decoders with `andThen` / custom code                          | Pipeline combinators (`gt`, `lt`, `minLength`, ...) — direct Pydantic Annotated equivalent |
| Error mode             | Fail-fast (first error)                                                | Accumulating (all per-field errors at once)                                                |
| JSON Schema generation | Not built in                                                           | `jsonSchemaOf<'T>`, from the same walk as decode/encode                                    |
| Backends               | JS, Python (Thoth.Json 10+), .NET                                      | BEAM, Python, JS, .NET                                                                     |
| Coercion               | Strict (types must match)                                              | `"42" → int 42` etc. (a Strict mode is planned)                                            |
| Maturity               | Years of production use, large user base                               | New                                                                                        |

If you want explicit hand-written decoders or a battle-tested option with broad third-party support, use Thoth. If your records are mostly straightforward and you want validation rules to live in the type, this library is the match.

### Performance vs. Thoth

From `just bench` (BenchmarkDotNet, `DefaultJob`, .NET 10). Absolute figures are machine-specific; the ratios are the portable part.

Thoth's reflection-driven path on .NET is Newtonsoft-backed while this library's .NET shim is System.Text.Json-backed, so a head-to-head ratio mixes parser choice with codec design. Each is therefore also shown against **its own** parser — the only column that says anything about the decoder itself.

| Flat 3-field record, decode | Mean | Allocated | Over its own parser |
| --- | ---: | ---: | ---: |
| System.Text.Json (raw) | 153 ns | 224 B | — |
| Thoth.Json.STJ (hand-written decoder) | 442 ns | 976 B | 2.9× |
| Newtonsoft (raw) | 467 ns | 3,056 B | — |
| **Fable.TypedJson (`auto`)** | **518 ns** | **1,072 B** | **3.4×** |
| Thoth.Json.Net (`Decode.Auto`) | 7,224 ns | 8,785 B | 15.5× |

The 14× end-to-end gap on that pair decomposes exactly into **4.6× decoder × 3.1× parser**. The honest claim is the first number: a ~4.6× advantage on the automatic path. The rest is Newtonsoft.

Two results worth stating plainly: `auto` allocates less per decode than *raw Newtonsoft* does, and lands within ~17% of a hand-written Thoth decoder while requiring no decoder at all.

| Other fixtures | Fable.TypedJson | Thoth.Json.Net (`Auto`) | vs. System.Text.Json |
| --- | ---: | ---: | ---: |
| nested decode — 2 levels + record list | 2.92 µs | 34.56 µs | 2.2× |
| flat encode | 252 ns | 8,679 ns | 2.7× |
| nested encode | 6.67 µs | 46.58 µs | 10.1× |

Only the flat-decode table is parser-decomposed; these three are end-to-end and carry the same Newtonsoft caveat. Flat encode is the one place a reflection-driven codec beats a hand-written one — Thoth's manual encoder is 592 ns — because `auto` writes straight into the backend map instead of building an intermediate tree first.

Both libraries are measured amortized: Thoth's `Auto` caches its generated coders internally, and these numbers build the `TypedJson<'T>` codec once outside the measured loop, as you should.

**Construction is the trade this design makes.** Resolving a type costs ~193 µs (flat) to ~1.03 ms (nested), most of it emitting delegates via `PreComputeRecordConstructor` on the CLR — which is what buys the ~12× per-decode win. Break-even is about **30 decodes of the same type**, so bind codecs at module level rather than per call.

### vs. [Fable.SimpleJson](https://github.com/Zaid-Ajaj/Fable.SimpleJson)

SimpleJson sits one rung lower: it parses JSON into a recursive `Json` AST and lets you pattern-match. It's closer to "JSON.parse and inspect" than to "validate against a record schema."

|                        | Fable.SimpleJson                                                               | Fable.TypedJson                                     |
| ---------------------- | ------------------------------------------------------------------------------ | --------------------------------------------------- |
| Output                 | Recursive `Json` AST you pattern-match on, plus reflection-based `parseAs<'T>` | Validated F# record                                 |
| Validation rules       | Whatever you write after parsing                                               | Encoded in the type via wrapper DUs and combinators |
| Errors                 | JSON parse errors only                                                         | Per-field validation errors with paths              |
| JSON Schema generation | Not built in                                                                   | Yes (constraint-aware)                              |
| Backends               | JS, .NET                                                                       | BEAM, Python, JS, .NET                              |

If you want a low-level JSON AST to inspect or you need maximum control over decoding, use SimpleJson. If you want type-driven validation with Pydantic-like ergonomics, use this.

## Architecture

Two design axes, each independent:

1. **Staged resolution** (vertical) — the library is a staged compiler. Building
   a codec walks `typeof<'T>` once and emits a tree of closures; decoding,
   encoding and describing then just run them. No reflection, no type-name
   comparison and no key derivation survives into the per-call path, at any
   depth. One walk produces all three faces, so they cannot disagree about a
   type's wire shape.
2. **Backend-agnostic core vs per-target shims** (horizontal) — `IJsonBackend`
   abstracts the actual JSON parser and the native map type. Concrete shims
   ship for BEAM (jsx), Python (`json`), JavaScript (`JSON.parse` /
   `JSON.stringify`), and .NET (`System.Text.Json`).

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

`IJsonBackend` exposes three groups of operations:

- **Map ops:** `NewMap`, `Get`, `Put`, `ContainsKey`, `ParseRaw`, `Stringify`, `Null`.
- **Type tests:** `IsString` / `IsInt` / `IsFloat` / `IsBool` / `IsNull` / `IsArray` / `IsMap`.
- **Typed accessors:** `AsString` / `AsInt` / `AsFloat` / `AsBool` — paired with the type tests so the schema can dispatch directly on the backend's native shape (Erlang binary, Python `str`, JS `string`, or a CLR `JsonValue` case) without going through a shared `[<Erase>]` DU pattern. The `JsonValue` DU is reserved for the user-facing `IJsonCodec` API; the internal hot path operates entirely on `obj` + the backend's accessor pair.

## Layout

- `src/Fable.TypedJson/` — backend-agnostic core
- `src/Fable.TypedJson.Beam/` — BEAM backend shim
- `src/Fable.TypedJson.Python/` — Python backend shim
- `src/Fable.TypedJson.JS/` — JavaScript backend shim
- `src/Fable.TypedJson.DotNet/` — .NET backend shim (System.Text.Json, runs natively on the CLR)
- `test/` — F# test sources. Compile to Erlang (`#if !DOTNET && !PYTHON && !JS` → BEAM), Python (`#if PYTHON`), JavaScript (`#if JS`), or run natively on the CLR (`#if DOTNET`, no Fable transpile). One [Scriptorium](https://github.com/fable-hub/Scriptorium) suite drives all four — Quill supplies the test DSL and runner (`Main.fs` is the single entry point), Nib the assertions.

## Prerequisites

### Using the library (NuGet consumer)

The core and the Fable shims (Beam, Python, JS) target **netstandard2.0**, so any .NET runtime that supports netstandard2.0 works — .NET 6 / 7 / 8 / 9 / 10, .NET Core 2.0+, .NET Framework 4.6.1+, Mono 5.4+. The .NET shim (`Fable.TypedJson.DotNet`) targets **net10.0** because it runs natively on the CLR (no Fable transpile) and pulls in `System.Text.Json`. For the Fable shims, you also need the Fable toolchain to transpile to BEAM / Python / JS.

### Building this repo from source

- .NET SDK that satisfies the test fsproj's `net10.0` target (currently .NET SDK 10)
- For the BEAM target: Erlang/OTP and `rebar3`
- For the Python target: `uv` (the venv pulls in `fable-library`)
- `just` (task runner)

## Setup

```sh
just restore        # dotnet tools (Fable, Paket, Fantomas) + Paket deps + uv venv
```

Paket deps are split into five groups (`Main`, `Beam`, `Python`, `JS`, `DotNet`) so each backend project pulls in only what it needs — see `paket.dependencies`.

## Build

```sh
just build          # transpile core + Beam to Erlang, core + Python to Python, core + JS to JavaScript
just build-beam     # only the BEAM pipeline (Fable + rebar3)
just build-python   # only the Python pipeline (Fable, no further compile step)
just build-js       # only the JavaScript pipeline (Fable, no further compile step)
just check          # type-check all five projects via `dotnet build` (DotNet is the only one that also runs natively)
```

## Test

```sh
just test           # run all four backend test suites from the same F# sources
just test-beam      # transpile tests to Erlang, run on the BEAM VM
just test-python    # transpile tests to Python, run under python
just test-js        # transpile tests to JavaScript, run under node
just test-dotnet    # build the .NET test project for net10.0, run natively on the CLR
```

The same F# test sources compile to all four targets via `#if PYTHON | JS | DOTNET` blocks that swap a few backend-specific imports. Tests are written with [Scriptorium](https://github.com/fable-hub/Scriptorium) — Quill for the test DSL and runner, Nib for assertions — both of which compile to every target, so a single `runTests` entry point in `Main.fs` replaces the per-target runners. Quill exits non-zero on failure on all four targets, so CI gates on it.

Known per-target divergences are marked with Quill's `skipIfJavaScript` / `skipIfDotNet` configurers next to the test, each carrying a comment explaining the gap, so they show up as skips rather than silently disappearing.

## Format

```sh
just format
```
