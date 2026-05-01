# Fable.TypedJson

Pydantic-flavored JSON validation and serialization for F# records, designed for Fable's multi-backend output. **BEAM (Erlang) and Python work today**; JS and .NET shims are a small addition away on top of the same `IJsonBackend` abstraction.

The headline idea: **validation lives with the type**. Define a wrapper DU, ship a `JsonCodec` static member on it, and `auto<'T>()` discovers it and dispatches through it — the F# answer to Pydantic's "custom types with embedded validators."

## Quick example

```fsharp
open Fable.TypedJson.Schema      // IJsonCodec, emptyRegistry, register, formatErrors
open Fable.TypedJson.Refined     // NonEmptyString, PositiveInt, Email, Url, Uuid
open Fable.TypedJson.Json        // CaseRules, withModel, alias

// Pick the backend convenience module for your Fable target.
open Fable.TypedJson.Beam.Json   // or  Fable.TypedJson.Python.Json

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
let codec = autoWith<WeatherRequest> codecs

match codec.decode CaseRules.SnakeCase jsonMap with
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

let schemaJson = jsonSchemaOf<Account> codecs CaseRules.SnakeCase
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

For aliases attached via `TypedJson.alias`, use `jsonSchemaOfCodec codec` so the property names match the codec's actual JSON keys.

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
  `string -> JsonValue option` lookup to `Result<'T, FieldError list>`. It
  walks the F# record's reflection, applies the registered codecs, and
  accumulates errors. It doesn't know anything about JSON.
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
- **Multi-backend by design**: an `IJsonBackend` abstraction in the core, with concrete shims for BEAM (jsx) and Python (`json`). JS and .NET shims are a small addition.
- **Errors accumulate**: a single `Error` result lists every per-field problem with a `path`, not just the first.
- **String-coerced primitives**: `JString "42"` decodes as `int 42`. Useful for LLM tool calls and shell input where everything arrives as strings.
- **JSON Schema generation**: `jsonSchemaOf<'T>` / `jsonSchemaOfCodec codec` walks reflection + the registry to produce a JSON Schema doc, with combinator constraints (`minLength`, `pattern`, `exclusiveMinimum`, ...) folded in.
- **Cross-field validators**: `withModel (fun r -> ...)` for invariants that span fields.
- **Field aliases**: `alias "FieldName" "json_key"` overrides the JSON-key derivation per field. Reflected in decode, encode, and the generated schema.

## Case rules

Field names from F# reflection are transformed into JSON keys via a `CaseRules` argument at decode/encode time. The rule normalizes names through PascalCase internally so the same rule produces consistent output regardless of how the backend's reflection presents the F# name (BEAM lowercases, Python preserves):

| Rule               | Input            | Output           |
| ------------------ | ---------------- | ---------------- |
| `None`             | `MyField`        | `MyField`        |
| `LowerFirst`       | `MyField`        | `myField`        |
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
| Backends               | JS, Python (Thoth.Json 10+), .NET                                      | BEAM, Python today; JS and .NET planned                                                    |
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
| Backends               | JS, .NET                                                                       | BEAM, Python today; more planned                    |

If you want a low-level JSON AST to inspect or you need maximum control over decoding, use SimpleJson. If you want type-driven validation with Pydantic-like ergonomics, use this.

## Architecture

Two design axes, each independent:

1. **Schema vs TypedJson layering** (vertical) — `Schema` is format-agnostic
   record validation; `TypedJson` is the JSON shell on top. You can stop at
   `Schema` if you're validating a `Map<string, string>` from an LLM tool
   call or a dict from elsewhere — see [Schema vs TypedJson](#schema-vs-typedjson--validate-dicts-and-maps-too) above.
2. **Backend-agnostic core vs per-target shims** (horizontal) — `IJsonBackend`
   abstracts the actual JSON parser and the native map type. Concrete shims
   ship for BEAM (jsx) and Python (`json`); JS and .NET are a similarly
   sized addition.

```text
Fable.TypedJson (core, no backend deps)
├── Backend        IJsonBackend interface (Parse, Stringify, Get, Put, IsX, ArrayLength, ...)
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
```

`IJsonBackend` exposes both map operations (`NewMap`, `Get`, `Put`, `ContainsKey`, `Parse`/`Stringify`) and runtime type tests (`IsString`, `IsInt`, `IsFloat`, `IsBool`, `IsNull`, `IsArray`, `IsMap`) so the schema layer can dispatch correctly across backends without depending on `[<Erase>]` JsonValue patterns surviving codegen.

## Layout

- `src/Fable.TypedJson/` — backend-agnostic core
- `src/Fable.TypedJson.Beam/` — BEAM backend shim
- `src/Fable.TypedJson.Python/` — Python backend shim
- `test/` — F# test sources, transpiled to Erlang (BEAM) or Python via `#if PYTHON` conditional
- `test_python/` — pytest harness (`conftest.py`) that picks up the Fable-emitted `test_*.py` files

## Prerequisites

- .NET SDK 10
- For the BEAM target: Erlang/OTP and `rebar3`
- For the Python target: `uv` (the venv pulls in `fable-library` and `pytest`)
- `just` (task runner)

## Setup

```sh
just restore        # dotnet tools (Fable, Paket, Fantomas) + Paket deps + uv venv
```

Paket deps are split into three groups (`Main`, `Beam`, `Python`) so each backend project pulls in only what it needs — see `paket.dependencies`.

## Build

```sh
just build          # transpile core + Beam to Erlang, transpile core + Python to Python
just build-beam     # only the BEAM pipeline (Fable + rebar3)
just build-python   # only the Python pipeline (Fable, no further compile step)
just check          # type-check all three projects via `dotnet build`
```

## Test

```sh
just test           # run both backend test suites (143 tests each, identical sources)
just test-beam      # transpile tests to Erlang, run via test_runner.erl on BEAM
just test-python    # transpile tests to Python, run via pytest
```

The same F# test sources compile to both targets via `#if PYTHON` blocks that swap a few backend-specific imports. `Fable.Core.Testing.Assert.AreEqual` doesn't throw on Fable BEAM, so the test helpers in `test/Testing.fs` raise explicitly on inequality — making both runners report real failures.

## Format

```sh
just format
```
