# Fable.TypedJson

[![Build and Test](https://github.com/fable-hub/Fable.TypedJson/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/fable-hub/Fable.TypedJson/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/Fable.TypedJson.svg)](https://www.nuget.org/packages/Fable.TypedJson)

Type-driven JSON validation and serialization for F# on **BEAM, Python,
JavaScript, and .NET**, with Pydantic-style validation through refined types.

Derive a decoder and encoder from your F# model, then generate JSON Schema using
the same planning logic. Add constraints through custom codecs, choose field
naming conventions, and report validation errors with paths.

## Install

Install the core package and the adapter for your target:

```sh
dotnet add package Fable.TypedJson
dotnet add package Fable.TypedJson.Beam
```

| Target | Adapter package | JSON implementation |
| --- | --- | --- |
| BEAM (Erlang) | `Fable.TypedJson.Beam` | jsx |
| Python | `Fable.TypedJson.Python` | `json` |
| JavaScript | `Fable.TypedJson.JS` | `JSON.parse` / `JSON.stringify` |
| .NET | `Fable.TypedJson.DotNet` | `System.Text.Json` |

The core and Fable adapters target `netstandard2.0`; the .NET adapter targets
`net10.0`. See [Getting started](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/getting-started.md)
for imports and a complete round trip.

## Quick start

```fsharp
open Fable.TypedJson.Json
open Fable.TypedJson.Beam.Json // or .Python.Json / .JS.Json / .DotNet.Json

type Reading = { Location: string; AirTemperature: float }

let codec = auto<Reading> ()

let decoded =
    decodeText codec """{"location":"Oslo","airTemperature":22.5}"""
// Ok { Location = "Oslo"; AirTemperature = 22.5 }

let encoded = codec.encode { Location = "Oslo"; AirTemperature = 22.5 }
// JSON text: {"location":"Oslo","airTemperature":22.5}
```

`auto` accepts primitive coercion, such as `"22.5"` for a `float` field. Use
`autoStrict` when JSON primitive types must match the model. `decodeText`
distinguishes malformed JSON from typed validation errors.

Build and configure a codec once, then reuse it. On BEAM, retain it in an
application or actor owner; a compiled module-level accessor can rebuild its
value on each call.

## Documentation

Start with the [Overview](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/overview.md),
then follow the concepts you need:

| Guide | What you will learn |
| --- | --- |
| [Getting started](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/getting-started.md) | Install an adapter and decode, encode, and describe a model |
| [Decoding](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/decoding.md) | JSON text, parsed values, string maps, strictness, and errors |
| [Validation](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/validation.md) | Refined types, custom codecs, registries, and model validators |
| [Wire format](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/wire-format.md) | Naming, aliases, options, collections, scalars, and tagged unions |
| [JSON Schema](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/json-schema.md) | Constraints, recursive definitions, and OpenAPI integration |
| [Architecture](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/architecture.md) | Codec planning, backend interfaces, and optimization boundaries |
| [Performance](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/performance.md) | Codec ownership and how to interpret benchmarks |
| [Choosing a JSON library](https://github.com/fable-hub/Fable.TypedJson/blob/main/docs/comparison.md) | How this approach relates to Thoth.Json and Fable.SimpleJson |

For repository setup and changes, read
[Contributing](https://github.com/fable-hub/Fable.TypedJson/blob/main/CONTRIBUTING.md).
