(**
# Fable.TypedJson.DotNet.Json — .NET-baked TypedJson convenience layer

Wraps `Fable.TypedJson.Json.auto` / `validate` / `Encode` with the .NET
backend pre-applied so callers don't have to thread `IJsonBackend` through
every call. Mirrors `Fable.TypedJson.JS.Json` and `Fable.TypedJson.Beam.Json`.

Open this **after** `Fable.TypedJson.Json` so the .NET-baked versions
shadow the core ones.
*)

module Fable.TypedJson.DotNet.Json

open Fable.TypedJson.Backend
open Fable.TypedJson.Json
open Fable.TypedJson.Schema

let dotnet: IJsonBackend = Fable.TypedJson.DotNet.Backend.dotnet

/// Parse a JSON string into the backend's native map representation.
/// Equivalent to `dotnet.ParseRaw json`; provided for convenience.
let parseRaw (json: string) : JsonMap = dotnet.ParseRaw json

let inline auto<'T> () : TypedJson<'T> = Fable.TypedJson.Json.auto<'T> dotnet

let inline autoWith<'T> (registry: CodecRegistry) : TypedJson<'T> =
    Fable.TypedJson.Json.autoWith<'T> dotnet registry

let inline validate<'T> (map: JsonMap) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validate<'T> dotnet map

let inline validateWith<'T> (registry: CodecRegistry) (map: JsonMap) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateWith<'T> dotnet registry map

let inline validateJson<'T> (map: obj) : Result<'T, FieldError list> =
    Fable.TypedJson.Schema.validateJson<'T> dotnet map

let inline validateMap<'T> (map: Map<string, string>) : Result<'T, FieldError list> =
    Fable.TypedJson.Schema.validateMap<'T> dotnet map

let inline dump<'T> (record: 'T) : obj =
    Fable.TypedJson.Json.dump<'T> dotnet record

/// Generate a JSON Schema document for record type `'T`. Uses the supplied
/// `CodecRegistry` for custom-codec types and `caseRules` to map F# field
/// names to JSON keys. For codec-attached aliases / case rule, use
/// `jsonSchemaOfCodec` instead.
let inline jsonSchemaOf<'T> (registry: CodecRegistry) (caseRules: CaseRules) : string =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaOf<'T> dotnet registry caseRules

/// Generate a JSON Schema document from a `TypedJson<'T>` codec. Reads the
/// codec's configured `caseRules` and any `alias` overrides so the schema
/// matches what the codec accepts and produces.
let inline jsonSchemaOfCodec<'T> (registry: CodecRegistry) (codec: TypedJson<'T>) : string =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaOfCodec<'T> dotnet registry codec

module Encode =
    let inline string s = Fable.TypedJson.Json.Encode.string s
    let inline int n = Fable.TypedJson.Json.Encode.int n
    let inline float f = Fable.TypedJson.Json.Encode.float f
    let inline bool b = Fable.TypedJson.Json.Encode.bool b

    let inline list (encoder: 'T -> obj) (items: 'T list) =
        Fable.TypedJson.Json.Encode.list dotnet encoder items

    let inline optional (encoder: 'T -> obj) (v: 'T option) =
        Fable.TypedJson.Json.Encode.optional dotnet encoder v

    let object (fields: (string * obj) list) =
        Fable.TypedJson.Json.Encode.object dotnet fields

    let raw (jsonStr: string) =
        Fable.TypedJson.Json.Encode.raw dotnet jsonStr

    let toJson (term: obj) =
        Fable.TypedJson.Json.Encode.toJson dotnet term
