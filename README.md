# Fable.TypedJson

Pydantic-flavored JSON validation and serialization for F# records, designed for Fable's multi-backend output. **BEAM (Erlang), Python, JavaScript, and .NET all work today**, sharing a single `IJsonBackend` abstraction.

The headline idea: **validation lives with the type**. Define a wrapper DU, ship a `JsonCodec` static member on it, and `auto<'T>()` discovers it and dispatches through it — the F# answer to Pydantic's "custom types with embedded validators."

## Quick example

```fsharp
open Fable.TypedJson.Schema      // IJsonCodec, emptyRegistry, register, formatErrors
open Fable.TypedJson.Refined     // NonEmptyString, PositiveInt, Email, Url, Uuid
open Fable.TypedJson.Json        // CaseRules, withModel, alias

// Pick the backend convenience module for your target.
open Fable.TypedJson.Beam.Json   // or .Python.Json / .JS.Json (Fable targets) / .DotNet.Json (native CLR)

// We don't `open Fable.TypedJson.Codec`: its `int`, `string`, `bool` codec values
// would shadow F#'s primitive type conversions. Use the qualified `Codec.` prefix.
module Codec = Fable.TypedJson.Codec

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
    |> Refined.registerAll              // NonEmptyString, PositiveInt, Email, Url, ...

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

## Schema vs TypedJson — validate dicts and maps too

Internally there are two layers, and you can stop at the bottom one:

- **`Schema`** — format-agnostic. A `Schema<'T>` is just a function from a
  `string -> obj option` lookup to `Result<'T, FieldError list>`. It
  walks the F# record's reflection, applies the registered codecs, and
  accumulates errors. It doesn't know anything about JSON; the lookup
  hands back native values (Erlang binary, Python `str`, JS string, …)
  and the schema dispatches via the backend's `IsX` / `AsX` methods.
- **`TypedJson`** — JSON-specific shell on top: adds `CaseRules`, the
  encode side, `alias`, `withModel`, and the `Encode.toJson` / `parseRaw`
  plumbing.

Because `Schema` only needs a key→value lookup, it validates any source
that fits that shape. The library ships with two adapters out of the box:

```fsharp
// Map<string, string> — LLM tool-call inputs, form fields, env vars, ...
// Every value arrives as a string; primitive codecs coerce to int / float /
// bool / etc. via `Type coercion` below.
let toolInput : Map<string, string> =
    Map.ofList [ "location", "Oslo"; "days", "3"; "detailed", "true" ]

match validateMap<WeatherRequest> toolInput with
| Ok req -> handle req
| Error errs -> printfn "%s" (formatErrors errs)
```

```fsharp
// Backend-native JSON map (a parsed jsx map / Python dict / ...)
let jsonMap = parseRaw """{"location":"Oslo","days":3}"""

match validateJson<WeatherRequest> jsonMap with
| Ok req -> handle req
| Error errs -> printfn "%s" (formatErrors errs)
```

Both go through the same `Schema.auto<'T>`. The difference is just the
adapter that turns the source map into the lookup function. Building your
own adapter for env-var dicts, query strings, or BEAM proplists is a few
lines of F#. Pydantic users coming from the LLM tool-call use case will
recognize this — it's the same role `model_validate` plays for dict
inputs in Pydantic.

## Approach

- **Validators-as-types**: each field's type carries its rules. Reuse across records, compose across libraries.
- **Pipeline combinators**: `Codec.gt`, `Codec.lt`, `Codec.ge`, `Codec.le`, `Codec.minLength`, `Codec.maxLength`, `Codec.nonEmpty`, `Codec.pattern`, `Codec.refine`, `Codec.map`, `Codec.describe`. Apply to any `IJsonCodec<'T>`.
- **Reflection-driven `auto<'T>`**: walks F# record fields, recurses into nested records, handles `'T list` / `'T[]` / `'T option`, and looks up custom-typed fields via the registry. No per-record boilerplate.
- **Bundled refined types**: `NonEmptyString`, `PositiveInt`, `NonNegativeInt`, `Email`, `Url`, `Uuid` — register them all in one call (`Refined.registerAll`) or pick à la carte.
- **Multi-backend by design**: an `IJsonBackend` abstraction in the core, with concrete shims for BEAM (jsx), Python (`json`), JavaScript (`JSON.parse` / `JSON.stringify`), and .NET (`System.Text.Json`).
- **Errors accumulate**: a single `Error` result lists every per-field problem with a `path`, not just the first.
- **String-coerced primitives**: a JSON `"42"` decodes as `int 42`. Useful for LLM tool calls and shell input where everything arrives as strings.
- **JSON Schema generation**: `jsonSchemaOf<'T>` / `jsonSchemaOfCodec codec` walks reflection + the registry to produce a JSON Schema doc, with combinator constraints (`minLength`, `pattern`, `exclusiveMinimum`, ...) folded in.
- **Cross-field validators**: `withModel (fun r -> ...)` for invariants that span fields.
- **Field aliases**: `alias "FieldName" "json_key"` overrides the JSON-key derivation per field. Reflected in decode, encode, and the generated schema.

## Case rules

Field names from F# reflection are transformed into JSON keys via a `CaseRules` setting on the codec. The default is `LowerFirst` (camelCase) — the most common convention for modern JSON APIs. Use `withCaseRules` to switch.

```fsharp
// Default (camelCase): no extra step needed
let camel = auto<WeatherRequest> beam
camel.encode { Location = "Oslo"; Days = 3 }
// {"location":"Oslo","days":3}

// Snake_case
let snake = auto<WeatherRequest> beam |> withCaseRules CaseRules.SnakeCase
snake.encode { Location = "Oslo"; Days = 3 }
// {"location":"Oslo","days":3}      ← single-word fields look the same

// Multi-word fields show the difference:
type Reading = { AirTemperature: float; WindSpeed: float }
(auto<Reading> beam |> withCaseRules CaseRules.SnakeCase).encode { ... }
// {"air_temperature":22.5,"wind_speed":3.0}

// One-off override (rare — same codec, multiple JSON formats):
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

`Schema.formatErrors` turns the list into a single human-readable string — handy for surfacing back to an LLM as a tool error, or to a user as a form-validation summary.

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
| JSON Schema generation | Not built in                                                           | `jsonSchemaOf<'T>` walks reflection + registry                                             |
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

## Architecture

Two design axes, each independent:

1. **Schema vs TypedJson layering** (vertical) — `Schema` is format-agnostic
   record validation; `TypedJson` is the JSON shell on top. You can stop at
   `Schema` if you're validating a `Map<string, string>` from an LLM tool
   call or a dict from elsewhere — see [Schema vs TypedJson](#schema-vs-typedjson--validate-dicts-and-maps-too) above.
2. **Backend-agnostic core vs per-target shims** (horizontal) — `IJsonBackend`
   abstracts the actual JSON parser and the native map type. Concrete shims
   ship for BEAM (jsx), Python (`json`), JavaScript (`JSON.parse` /
   `JSON.stringify`), and .NET (`System.Text.Json`).

```text
Fable.TypedJson (core, no backend deps)
├── Backend        IJsonBackend interface — map operations, IsX type tests, AsX accessors
├── Schema         format-agnostic validation: coerce, resolveField, auto, registry, refined
├── Codec          IJsonCodec primitives + pipeline combinators (gt, lt, minLength, ...)
├── Refined        bundled refined types (NonEmptyString, PositiveInt, Email, Url, Uuid)
├── TypedJson      JSON layer: CaseRules, encode/decode wiring, alias, withModel
└── JsonSchemaGen  reflection-driven JSON Schema doc generation

Fable.TypedJson.Beam (BEAM shim)
├── Backend        BeamBackend implementing IJsonBackend (jsx + Fable.Beam.Maps)
└── Json           backend-baked `auto`, `autoWith`, `validate`, `Encode`, `parseRaw`,
                   `jsonSchemaOf`, `jsonSchemaOfCodec`

Fable.TypedJson.Python (Python shim)
├── Backend        PythonBackend implementing IJsonBackend (json.loads / json.dumps)
└── Json           same convenience surface as the BEAM shim, with python pre-applied

Fable.TypedJson.JS (JavaScript shim)
├── Backend        JSBackend implementing IJsonBackend (JSON.parse / JSON.stringify, native object/array)
└── Json           same convenience surface as the BEAM shim, with js pre-applied

Fable.TypedJson.DotNet (.NET shim — runs natively on the CLR, no Fable transpile)
├── Backend        DotNetBackend implementing IJsonBackend (System.Text.Json: JsonDocument + Utf8JsonWriter)
└── Json           same convenience surface, with dotnet pre-applied
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
just test-dotnet    # build tests for net10.0 with FableTarget=dotnet, run natively on the CLR
```

The same F# test sources compile to all four targets via `#if PYTHON | JS | DOTNET` blocks that swap a few backend-specific imports. Tests are written with [Scriptorium](https://github.com/fable-hub/Scriptorium) — Quill for the test DSL and runner, Nib for assertions — both of which compile to every target, so a single `runTests` entry point in `Main.fs` replaces the per-target runners. Quill exits non-zero on failure on all four targets, so CI gates on it.

Known per-target divergences are marked with Quill's `skipIfJavaScript` / `skipIfDotNet` configurers next to the test, each carrying a comment explaining the gap, so they show up as skips rather than silently disappearing.

## Format

```sh
just format
```
