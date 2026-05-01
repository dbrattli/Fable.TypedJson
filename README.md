# Fable.TypedJson

Pydantic-flavored JSON validation and serialization for F# records, designed for Fable's multi-backend output (BEAM, Python, JavaScript, .NET).

The headline idea: **validation lives with the type**. Define a wrapper DU, ship a `JsonCodec` static member on it, and `auto<'T>()` discovers it and dispatches through it — the F# answer to Pydantic's "custom types with embedded validators."

## Quick example

```fsharp
open Fable.TypedJson.Schema      // IJsonCodec, emptyRegistry, register, formatErrors
open Fable.TypedJson.Refined     // NonEmptyString, PositiveInt, Email, ...
open Fable.TypedJson.Json        // CaseRules, withModel
open Fable.TypedJson.Beam.Json   // backend-baked auto / autoWith

// We don't `open Fable.TypedJson.Codec`: its `int`, `string`, `bool` codec values
// would shadow F#'s primitive type conversions. Use the qualified `Codec.` prefix.
module Codec = Fable.TypedJson.Codec

// 1. Define a wrapper type. The pipeline composes validators the same way
//    Pydantic composes  Annotated[int, Field(gt=0, le=14)].
type Days =
    | Days of int
    static member JsonCodec : IJsonCodec<Days> =
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
| Ok req      -> handle req
| Error errs  ->
    // [{ path = "days";     message = "must be > 0" };
    //  { path = "location"; message = "must be non-empty" }]
    printfn "%s" (formatErrors errs)
```

For one-off rules where a named wrapper type would be overkill, the same combinator pipeline is the F# Annotated equivalent — `Codec.int |> Codec.gt 0 |> Codec.le 14` plays the role of `Annotated[int, Field(gt=0, le=14)]`. (A `TypedJson.field` helper for inline-per-field overlays is on the roadmap; today the wrapper-type path is the supported one.)

## Approach

- **Validators-as-types**: each field's type carries its rules. Reuse across records, compose across libraries.
- **Pipeline combinators**: `Codec.gt`, `Codec.lt`, `Codec.minLength`, `Codec.maxLength`, `Codec.pattern`, `Codec.nonEmpty`, `Codec.refine`, `Codec.map`. Applies to any `IJsonCodec<'T>`.
- **Reflection-driven `auto<'T>`**: walks F# record fields and looks up each non-primitive field's codec via the registry. No per-record boilerplate.
- **Multi-backend by design**: an `IJsonBackend` abstraction in the core; today the BEAM shim ships in `Fable.TypedJson.Beam`. JavaScript, Python, and .NET shims are planned.
- **Errors accumulate**: a single `Error` result lists every per-field problem with a `path`, not just the first.
- **String-coerced primitives**: `JString "42"` decodes as `int 42`. Useful for LLM tool calls where everything arrives as strings.
- **Cross-field validators**: `withModel (fun r -> ...)` for invariants that span fields.

## Case rules

Field names from F# reflection (lowercase on BEAM) are transformed into JSON keys via a `CaseRules` argument at decode/encode time:

| Rule               | Input      | Output     |
| ------------------ | ---------- | ---------- |
| `None`             | `my_field` | `my_field` |
| `LowerFirst`       | `my_field` | `myField`  |
| `SnakeCase`        | `my_field` | `my_field` |
| `SnakeCaseAllCaps` | `my_field` | `MY_FIELD` |
| `KebabCase`        | `my_field` | `my-field` |
| `PascalCase`       | `my_field` | `MyField`  |

## Type coercion

Built-in primitive codecs accept several source types — useful when JSON input comes from LLM tool calls or shells where everything is a string:

| Target type |          Accepted sources           |
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

|                        |                               Thoth.Json                               |                                      Fable.TypedJson                                       |
| ---------------------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| Primary style          | Manual `Decode.field "x" Decode.string` decoders; `Auto<'T>` is opt-in | Reflection-driven `auto<'T>` is the primary path                                           |
| Per-type customization | Pass "extra coders" alongside the decoder                              | Define a wrapper DU's static `JsonCodec` member; `register` once                           |
| Constraint composition | Compose decoders with `andThen` / custom code                          | Pipeline combinators (`gt`, `lt`, `minLength`, ...) — direct Pydantic Annotated equivalent |
| Error mode             | Fail-fast (first error)                                                | Accumulating (all per-field errors at once)                                                |
| Backends               | JS, Python (Thoth.Json 10+), .NET                                      | BEAM today; JS / Python / .NET planned                                                     |
| Coercion               | Strict (types must match)                                              | `"42" → int 42` etc. (a Strict mode is planned)                                            |
| Maturity               | Years of production use, large user base                               | New                                                                                        |

If you want explicit hand-written decoders or a battle-tested option with broad third-party support, use Thoth. If your records are mostly straightforward and you want validation rules to live in the type, this library is the match.

### vs. [Fable.SimpleJson](https://github.com/Zaid-Ajaj/Fable.SimpleJson)

SimpleJson sits one rung lower: it parses JSON into a recursive `Json` AST and lets you pattern-match. It's closer to "JSON.parse and inspect" than to "validate against a record schema."

|                  |                                Fable.SimpleJson                                |                   Fable.TypedJson                   |
| ---------------- | ------------------------------------------------------------------------------ | --------------------------------------------------- |
| Output           | Recursive `Json` AST you pattern-match on, plus reflection-based `parseAs<'T>` | Validated F# record                                 |
| Validation rules | Whatever you write after parsing                                               | Encoded in the type via wrapper DUs and combinators |
| Errors           | JSON parse errors only                                                         | Per-field validation errors with paths              |
| Backends         | JS, .NET                                                                       | BEAM today; more planned                            |

If you want a low-level JSON AST to inspect or you need maximum control over decoding, use SimpleJson. If you want type-driven validation with Pydantic-like ergonomics, use this.

## Architecture

Two layers in the core, plus per-backend shims:

```text
Fable.TypedJson (core, no backend deps)
├── Schema         format-agnostic validation: coerce, resolveField, auto, registry
├── Codec          IJsonCodec primitives + pipeline combinators (gt, lt, minLength, ...)
├── Refined        bundled refined types (NonEmptyString, PositiveInt, Email, ...)
└── TypedJson      JSON layer: CaseRules, encode/decode wiring, withModel

Fable.TypedJson.Beam (per-backend shim)
├── Backend        BeamBackend implementing IJsonBackend (jsx + Fable.Beam.Maps)
├── Decode         BEAM-specific manual decoders (kept for backward compatibility)
└── Json           backend-baked `auto`, `autoWith`, `validate`, `Encode`
```

The `IJsonBackend` interface (in core) abstracts JSON parsing and the native map type. Each backend package supplies a concrete instance — today only BEAM exists; JS, Python, and .NET shims are next.

## Layout

- `src/Fable.TypedJson/` — backend-agnostic core
- `src/Fable.TypedJson.Beam/` — BEAM backend shim
- `test/` — tests transpiled to Erlang and run on BEAM via `rebar3`

## Prerequisites

- .NET SDK 10
- Erlang/OTP and `rebar3` (for the BEAM target)
- `just` (task runner)

## Setup

```sh
just restore
```

Restores `dotnet` tools (Fable, Paket, Fantomas) and Paket dependencies.

## Build

```sh
just build       # transpile both core and BEAM packages to Erlang, compile with rebar3
just check       # type-check via dotnet build
```

## Test

```sh
just test        # run BEAM test suite
```

## Format

```sh
just format
```
