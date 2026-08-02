# Fable.TypedJson

[![Build and Test](https://github.com/dbrattli/Fable.TypedJson/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/dbrattli/Fable.TypedJson/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/Fable.TypedJson.svg)](https://www.nuget.org/packages/Fable.TypedJson)

Pydantic-flavored JSON validation and serialization for F# records, designed for Fable's multi-backend output. **BEAM (Erlang), Python, JavaScript, and .NET all work today.**

Point it at a record type and it derives the decoder, the encoder, and a JSON Schema — from one walk of the type, so the three can't disagree.

```fsharp
open Fable.TypedJson.Json
open Fable.TypedJson.Beam.Json      // or .Python.Json / .JS.Json / .DotNet.Json

type Reading = { Location: string; AirTemperature: float }

let codec = auto<Reading> ()

codec.decode (parseRaw """{"location":"Oslo","airTemperature":22.5}""")
// Ok { Location = "Oslo"; AirTemperature = 22.5 }

codec.encode { Location = "Oslo"; AirTemperature = 22.5 }
// {"location":"Oslo","airTemperature":22.5}
```

## Install

Install the core package plus the shim for the target you compile to:

```sh
dotnet add package Fable.TypedJson
dotnet add package Fable.TypedJson.Beam     # pick one shim
```

| Target | Package | Runs on |
| --- | --- | --- |
| BEAM (Erlang) | `Fable.TypedJson.Beam` | Fable → Erlang, over jsx |
| Python | `Fable.TypedJson.Python` | Fable → Python, over `json` |
| JavaScript | `Fable.TypedJson.JS` | Fable → JS, over `JSON.parse` |
| .NET | `Fable.TypedJson.DotNet` | the CLR natively, over `System.Text.Json` |

Core and the Fable shims target **netstandard2.0**; `Fable.TypedJson.DotNet` targets **net10.0**. Opening the backend's `Json` module pre-applies the backend, so `auto` takes `()` and you never thread a backend value yourself.

**Build a codec once and reuse it.** Construction resolves the entire type tree — every nested record, list element and union case — so decoding does no reflection at any depth. Bind codecs at module level, not per request.

```fsharp
let codec = autoWith<WeatherRequest> codecs   // module level
```

## Validation lives with the type

The headline idea: define a wrapper DU, give it a `JsonCodec` static member, and `auto<'T>()` discovers it and dispatches through it — the F# answer to Pydantic's "custom types with embedded validators."

```fsharp
open Fable.TypedJson             // brings the module name `Codec` into scope
open Fable.TypedJson.Schema      // IJsonCodec, emptyRegistry, register, formatErrors
open Fable.TypedJson.Refined     // NonEmptyString, PositiveInt, Email, Url, Uuid
open Fable.TypedJson.Json        // auto, autoWith, CaseRules, withModel, alias
open Fable.TypedJson.Beam.Json

// 1. A wrapper type. The pipeline composes validators the same way
//    Pydantic composes  Annotated[int, Field(gt=0, le=14)].
type Days =
    | Days of int

    static member JsonCodec: IJsonCodec<Days> =
        Codec.int |> Codec.gt 0 |> Codec.le 14
        |> Codec.map Days (fun (Days n) -> n)

// 2. Use it like any field type. Optional fields handle missing-as-None natively.
type WeatherRequest = {
    Location: NonEmptyString          // bundled refined type
    Days: Days                        // user-defined validator
    Detailed: bool option
}

// 3. Build a registry once with the codecs your records use.
let codecs =
    emptyRegistry
    |> register Days.JsonCodec
    |> registerAll                    // NonEmptyString, PositiveInt, Email, Url, ...

// 4. Derive the codec and decode. Errors accumulate across all fields.
let codec = autoWith<WeatherRequest> codecs

match codec.decode jsonMap with
| Ok req -> handle req
| Error errs ->
    // [{ path = "days";     message = "must be > 0" };
    //  { path = "location"; message = "must be non-empty" }]
    printfn "%s" (formatErrors errs)
```

Use the `…With` forms whenever a record has refined or custom-codec fields — plain `auto` uses an empty registry, so a `NonEmptyString` or `Days` field has no codec to dispatch through.

For one-off rules where a named wrapper type would be overkill, the same pipeline is the F# `Annotated` equivalent inline: `Codec.int |> Codec.gt 0 |> Codec.le 14`.

**Available combinators:** `Codec.gt`, `lt`, `ge`, `le`, `minLength`, `maxLength`, `nonEmpty`, `pattern`, `refine`, `map`, `describe`. All apply to any `IJsonCodec<'T>`.

**Bundled refined types:** `NonEmptyString`, `PositiveInt`, `NonNegativeInt`, `Email`, `Url`, `Uuid` — register them all with `registerAll` (after `open Fable.TypedJson.Refined`) or pick à la carte.

## Errors

A single `Error` lists every per-field problem with a path, not just the first one:

```fsharp
Error [
    { path = "days";     message = "must be > 0" }
    { path = "location"; message = "must be non-empty" }
    { path = "contact";  message = "must match pattern '^[^\s@]+@[^\s@]+\.[^\s@]+$'" }
]
```

`formatErrors` turns the list into one human-readable string — handy for surfacing back to an LLM as a tool error, or to a user as a form-validation summary.

## Case rules

Field names from F# reflection become JSON keys via a `CaseRules` setting on the codec. The default is `LowerFirst` (camelCase). Use `withCaseRules` to switch.

```fsharp
type Reading = { AirTemperature: float; WindSpeed: float }

let reading = { AirTemperature = 22.5; WindSpeed = 3.0 }

(auto<Reading> ()).encode reading
// {"airTemperature":22.5,"windSpeed":3.0}

(auto<Reading> () |> withCaseRules CaseRules.SnakeCase).encode reading
// {"air_temperature":22.5,"wind_speed":3.0}

// One-off override (rare — same codec, multiple JSON formats). This rebuilds
// the codec, so hoist `withCaseRules` if it is on a hot path:
codec.decodeWith CaseRules.SnakeCaseAllCaps map
```

| Rule                   | Input      | Output     |
| ---------------------- | ---------- | ---------- |
| `None`                 | `MyField`  | `MyField`  |
| `LowerFirst` (default) | `MyField`  | `myField`  |
| `SnakeCase`            | `MyField`  | `my_field` |
| `SnakeCaseAllCaps`     | `MyField`  | `MY_FIELD` |
| `KebabCase`            | `MyField`  | `my-field` |
| `PascalCase`           | `my_field` | `MyField`  |

Names are normalized through PascalCase internally, so a rule produces the same output regardless of how a backend's reflection presents the F# name. Single-word fields look identical under either rule — multi-word names are the only place the difference shows.

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

`alias` flows through decode lookup, encode output, **and** the JSON Schema's `properties` / `required` keys. Field names passed to `alias` are normalized to PascalCase internally, so the same call works on every backend.

## Type coercion

Built-in primitive codecs accept several source types — useful when JSON comes from LLM tool calls or shells where everything arrives as a string:

| Target type | Accepted sources                    |
| ----------- | ----------------------------------- |
| `string`    | string, int, float, bool            |
| `int`       | int, float, string (parseable)      |
| `int64`     | int, float, string (parseable)      |
| `float`     | float, int, string (parseable)      |
| `bool`      | bool, string (`"true"` / `"false"`) |

## Tagged discriminated unions

An F# DU decodes and encodes as `{"type": "<case>", ...payload}` — the Pydantic / OpenAPI discriminated-union convention, and the shape Anthropic's and OpenAI's message formats use.

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

A single record-payload case flattens its fields alongside the discriminator; a fieldless case is just the discriminator. The case name goes through the codec's `CaseRules`, so `ToolUse` becomes `"tool_use"` under `SnakeCase`.

Two shapes are **not** supported in v1: a case with a non-record payload (`Circle of float`) and a case with multiple positional fields (`At of int * int`). Both are rejected when the codec is built, not when a document happens to select that case — a DU with a case that cannot round-trip is a broken codec either way, and finding out at construction beats finding out in production. Wrap the payload in a record, or register an `IJsonCodec` for the type.

## JSON Schema generation

Like Pydantic's `model_json_schema()`, a single call walks the codec tree and emits a JSON Schema document — handy for OpenAPI specs, LLM tool definitions, or runtime introspection. Constraints from the combinators (`minLength`, `pattern`, `gt`, ...) flow into the right schema keywords.

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

`jsonSchemaOfCodec` reads the codec's configured `caseRules` and any `alias` overrides, so the schema's property names always match the JSON the codec actually accepts and produces. (For a quick schema with no aliases or codec, `jsonSchemaOf<'T> codecs caseRules` takes the case rule explicitly.) `jsonSchemaOf<Tool>` on a DU emits a `oneOf` branch per case, each pinning its discriminator with `const`.

## Validating dicts and maps

The codec is the whole API — there is no second, weaker path. Two shorthands cover the common sources, both going through the same walker and the same key derivation a default codec uses:

```fsharp
// Map<string, string> — LLM tool-call inputs, form fields, env vars, ...
// Every value arrives as a string; primitive coercion turns it into the
// record's declared type (see `Type coercion` above).
let toolInput = Map.ofList [ "location", "Oslo"; "days", "3"; "detailed", "true" ]

match validateMapWith<WeatherRequest> codecs toolInput with
| Ok req -> handle req
| Error errs -> printfn "%s" (formatErrors errs)

// Backend-native JSON map (a parsed jsx map / Python dict / ...)
match validateJsonWith<WeatherRequest> codecs (parseRaw """{"location":"Oslo","days":3}""") with
| Ok req -> handle req
| Error errs -> printfn "%s" (formatErrors errs)
```

`dump` is the encode-side counterpart, producing a backend-native map instead of a string. All of these build a plan per call — for anything repeated, build a codec once and reuse it.

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

### Performance

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

## Architecture

Two design axes, each independent:

1. **Staged resolution** (vertical) — the library is a staged compiler. Building a codec walks `typeof<'T>` once and emits a tree of closures; decoding, encoding and describing then just run them. No reflection, no type-name comparison and no key derivation survives into the per-call path, at any depth. One walk produces all three faces, so they cannot disagree about a type's wire shape.
2. **Backend-agnostic core vs per-target shims** (horizontal) — `IJsonBackend` abstracts the actual JSON parser and the native map type. Concrete shims ship for BEAM (jsx), Python (`json`), JavaScript (`JSON.parse` / `JSON.stringify`), and .NET (`System.Text.Json`).

Adding a target means implementing `IJsonBackend` in a new `Fable.TypedJson.<Target>` project plus a `<Target>.Json` convenience module; the core does not change.

## Contributing

### Prerequisites

- .NET SDK 10 (the test projects target `net10.0`)
- `just` (task runner)
- BEAM target: Erlang/OTP and `rebar3`
- Python target: `uv` (the venv pulls in `fable-library`)

### Workflow

```sh
just restore        # dotnet tools (Fable, Paket, Fantomas) + Paket deps + uv venv
just build          # transpile core + each shim to Erlang, Python, JavaScript
just check          # type-check all five projects via `dotnet build`
just test           # run all four backend test suites from the same F# sources
just format         # Fantomas over src/ and test/
```

Per-target variants exist for each: `just build-beam` / `build-python` / `build-js`, and `just test-beam` / `test-python` / `test-js` / `test-dotnet`.

Paket deps are split into five groups (`Main`, `Beam`, `Python`, `JS`, `DotNet`) so each backend project pulls in only what it needs — see `paket.dependencies`.

The same F# test sources compile to all four targets via `#if PYTHON | JS | DOTNET` blocks that swap a few backend-specific imports. Tests are written with [Scriptorium](https://github.com/fable-hub/Scriptorium) — Quill for the test DSL and runner, Nib for assertions — both of which compile to every target, so a single `runTests` entry point in `Main.fs` replaces the per-target runners. Quill exits non-zero on failure on all four targets, so CI gates on it. Known per-target divergences are marked with Quill's `skipIfJavaScript` / `skipIfDotNet` configurers next to the test, each carrying a comment explaining the gap, so they show up as skips rather than silently disappearing.
