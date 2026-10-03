# Getting started

[Overview](overview.md) · [Decoding →](decoding.md)

This guide uses one `Reading` model to decode JSON, encode a value, and generate
a schema. The examples use the BEAM adapter; replace its import with the adapter
for your runtime.

## Install the packages

Add the core and one adapter to your application project:

```sh
dotnet add package Fable.TypedJson
dotnet add package Fable.TypedJson.Beam
```

| Target | Adapter | Import |
| --- | --- | --- |
| BEAM | `Fable.TypedJson.Beam` | `Fable.TypedJson.Beam.Json` |
| Python | `Fable.TypedJson.Python` | `Fable.TypedJson.Python.Json` |
| JavaScript | `Fable.TypedJson.JS` | `Fable.TypedJson.JS.Json` |
| .NET | `Fable.TypedJson.DotNet` | `Fable.TypedJson.DotNet.Json` |

The core and Fable adapters target `netstandard2.0`; the .NET adapter requires
`net10.0`. Use matching package versions for the core and adapter.

## Define and decode a model

```fsharp
open Fable.TypedJson.Schema
open Fable.TypedJson.Json
open Fable.TypedJson.Beam.Json

type Reading = { Location: string; AirTemperature: float }

let codec = auto<Reading> ()
let jsonText = """{"location":"Oslo","airTemperature":22.5}"""

let decoded = decodeText codec jsonText

match decoded with
| Ok reading -> printfn "%s: %.1f" reading.Location reading.AirTemperature
| Error (InvalidText message) -> printfn "Malformed JSON: %s" message
| Error (InvalidValue errors) -> printfn "%s" (formatErrors errors)
```

The adapter import comes last because its `auto` pre-applies the backend. The
core `Fable.TypedJson.Json.auto` accepts a backend argument; the adapter version
accepts `()`.

By default, `Location` becomes `location` and `AirTemperature` becomes
`airTemperature`. All non-optional fields must be present and non-null.

## Encode and round-trip

```fsharp
let reading = { Location = "Oslo"; AirTemperature = 22.5 }
let json = codec.encode reading
let roundTrip = decodeText codec json
// Ok { Location = "Oslo"; AirTemperature = 22.5 }
```

`encode` returns a string. JSON object property order is not part of the codec
contract. If your input is already parsed by the same backend, use
`codec.decode parsedValue` directly.

## Generate JSON Schema

```fsharp
let schemaJson = jsonSchemaOfCodec emptyRegistry codec
printfn "%s" schemaJson
```

The schema describes an object with required `location` and `airTemperature`
properties of type `string` and `number`. Pass the same registry to schema
generation that you used to build the codec; this example uses an empty one.

## Choose strictness and retain the codec

`auto` permits primitive coercion: a parseable string can become a number, for
example. Use `autoStrict<Reading> ()` to require matching JSON primitive types.
For custom field types, use `autoWith<Reading> registry` or
`autoStrictWith<Reading> registry`.

Construct and configure codecs outside repeated request handling. A module
binding works on JavaScript, Python, and .NET. On BEAM, construct during
application or actor startup and retain the codec in that owner; a module-level
accessor may construct it again when called.

Continue with [Decoding](decoding.md) for input sources and error behavior,
or [Validation](validation.md) to add rules to field types.
