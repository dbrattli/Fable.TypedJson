# Decoding

[Overview](overview.md) · [Getting started](getting-started.md) · [Validation →](validation.md)

Choose the entry point for the input you already have. JSON text needs parsing;
parsed JSON can go directly to the codec; forms and environment variables often
arrive as a `Map<string, string>`.

## JSON text and parsed values

```fsharp
open Fable.TypedJson.Schema
open Fable.TypedJson.Json
open Fable.TypedJson.Beam.Json

type Reading = { Location: string; AirTemperature: float }

let codec = auto<Reading> ()
let text = """{"location":"Oslo","airTemperature":22.5}"""

let fromText = decodeText codec text
let fromParsed = codec.decode (parseRaw text)
```

`decodeText` returns `Result<'T, JsonTextError>`:

| Result | Meaning |
| --- | --- |
| `Ok value` | Parsing and typed validation succeeded |
| `Error (InvalidText message)` | The backend parser threw while reading the text |
| `Error (InvalidValue errors)` | Parsing succeeded, but the value failed typed validation |

`parseRaw` is the lower-level parser and can throw. Only parser exceptions are
translated into `InvalidText`; exceptions from decoders, custom codecs, and model
validators propagate. Handle invalid input by returning `Error` from your own
validators.

## Coercing and strict modes

`auto` and `autoWith` use coercing primitive decoding:

| F# type | Accepted primitive sources |
| --- | --- |
| `string` | String, integer, float, Boolean |
| `int` | Integer, float, parseable string |
| `int64` | Integer, float, parseable string |
| `float` | Float, integer, parseable string |
| `bool` | Boolean, string `"true"` / `"false"` |

Use `autoStrict` or `autoStrictWith` when incoming JSON primitive types must
match the model:

```fsharp
let strictCodec = autoStrict<Reading> ()

let rejected =
    decodeText strictCodec """{"location":42,"airTemperature":"22.5"}"""
// InvalidValue errors at location and airTemperature
```

Strict mode rejects numbers and Booleans for strings, strings for numbers and
Booleans, and fractional or out-of-range numbers for integer fields. Integral
JSON numbers remain valid for `float` fields. Strictness follows nested records,
collections, and supported unions. For registered codecs, the guard uses the
codec's declared simple schema `type`; a custom codec remains responsible for
its own deeper validation.

Strict mode does not require every accepted value to have the exact schema wire
representation. For example, built-in `decimal` decoding still accepts strings
and JSON numbers; encoding uses strings. See [Scalar values](wire-format.md#scalar-values).

## String maps

String maps deliberately remain coercing, including on a strict codec:

```fsharp
let fields = Map.ofList [ "location", "Oslo"; "airTemperature", "22.5" ]
let fromFields = strictCodec.decodeStringMap fields
// Ok { Location = "Oslo"; AirTemperature = 22.5 }

let snakeCodec = codec |> withCaseRules CaseRules.SnakeCase
let snakeFields = Map.ofList [ "location", "Oslo"; "air_temperature", "22.5" ]
let fromSnakeFields = snakeCodec.decodeStringMap snakeFields
```

The codec's case rules, aliases, registry, and model validators also apply to
string maps. Matching uses the configured key exactly: under `SnakeCase`,
`airTemperature` does not satisfy a required `air_temperature` field.
String-map values are strings, not nested JSON documents; this is primarily a
path for models with scalar fields.

## Errors and paths

Records attempt every field and accumulate validation errors. A list or array
stops at its first failing element to avoid decoding the remaining tail after
failure. If that element is a record, it can contribute multiple field errors;
later collection elements are skipped. Other fields in the containing record
are still checked. Nested paths include field names and zero-based indices,
such as `groups[1].items[2]`.

```fsharp
let invalidText = """{"location":null,"airTemperature":"cold"}"""

match decodeText codec invalidText with
| Ok reading -> printfn "%A" reading
| Error (InvalidText message) -> printfn "Malformed JSON: %s" message
| Error (InvalidValue errors) ->
    for error in errors do
        printfn "%s: %s" error.path error.message
```

Missing or null required fields fail. Missing or null optional record fields
become `None`. Extra object keys are ignored; strict mode controls primitive
types and does not reject unknown properties.

## One-off shortcuts

| Helper | Input or output | Configuration |
| --- | --- | --- |
| `validateJson<'T>` / `validateJsonWith<'T>` | Decode parsed JSON | camelCase; the `With` form accepts a registry |
| `validateMap<'T>` / `validateMapWith<'T>` | Decode string maps | camelCase; the `With` form accepts a registry |
| `validateMapWithCaseRules<'T>` | Decode string maps | Explicit case rule, empty registry |
| `dump<'T>` / `dumpWith<'T>` | Encode to a native JSON value | camelCase; the `With` form accepts a registry |

These helpers build a plan per call and use coercing decoding. They do not
retain codec aliases or model validators. For repeated calls, strict JSON, or
combined configuration, construct a codec and use its methods.
