(**
# Fable.TypedJson.Python.Json — Python-baked TypedJson convenience layer

Wraps `Fable.TypedJson.Json.auto` / `validate` / `Encode` with the Python
backend pre-applied so users don't have to thread `IJsonBackend` through
every call. Open this **after** `Fable.TypedJson.Json` so the
Python-baked versions shadow the core ones.
*)

module Fable.TypedJson.Python.Json

open Fable.TypedJson.Backend
open Fable.TypedJson.Json
open Fable.TypedJson.Schema

let python: IJsonBackend = Fable.TypedJson.Python.Backend.python

/// Parse a JSON string into the backend's native map representation.
/// Equivalent to `python.ParseRaw json`; provided for convenience.
let parseRaw (json: string) : JsonMap = python.ParseRaw json

let inline auto<'T> () : TypedJson<'T> = Fable.TypedJson.Json.auto<'T> python

let inline autoWith<'T> (registry: CodecRegistry) : TypedJson<'T> =
    Fable.TypedJson.Json.autoWith<'T> python registry

let inline validate<'T> (map: JsonMap) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validate<'T> python map

let inline validateWith<'T> (registry: CodecRegistry) (map: JsonMap) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateWith<'T> python registry map

let inline validateJson<'T> (map: obj) : Result<'T, FieldError list> =
    Fable.TypedJson.Schema.validateJson<'T> python map

let inline validateMap<'T> (map: Map<string, string>) : Result<'T, FieldError list> =
    Fable.TypedJson.Schema.validateMap<'T> python map

let inline dump<'T> (record: 'T) : obj =
    Fable.TypedJson.Schema.dump<'T> python record

/// Generate a JSON Schema document for record type `'T`. Uses the supplied
/// `CodecRegistry` for custom-codec types and `caseRules` to map F# field
/// names to JSON keys. For codec-attached aliases / case rule, use
/// `jsonSchemaOfCodec` instead.
let inline jsonSchemaOf<'T> (registry: CodecRegistry) (caseRules: CaseRules) : string =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaOf<'T> python registry caseRules

/// Generate a JSON Schema document from a `TypedJson<'T>` codec. Reads the
/// codec's configured `caseRules` and any `alias` overrides so the schema
/// matches what the codec accepts and produces.
let inline jsonSchemaOfCodec<'T> (registry: CodecRegistry) (codec: TypedJson<'T>) : string =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaOfCodec<'T> python registry codec

module Encode =
    let inline string s = Fable.TypedJson.Json.Encode.string s
    let inline int n = Fable.TypedJson.Json.Encode.int n
    let inline float f = Fable.TypedJson.Json.Encode.float f
    let inline bool b = Fable.TypedJson.Json.Encode.bool b

    let inline list (encoder: 'T -> obj) (items: 'T list) =
        Fable.TypedJson.Json.Encode.list python encoder items

    let inline optional (encoder: 'T -> obj) (v: 'T option) =
        Fable.TypedJson.Json.Encode.optional python encoder v

    let object (fields: (string * obj) list) =
        Fable.TypedJson.Json.Encode.object python fields

    let raw (jsonStr: string) =
        Fable.TypedJson.Json.Encode.raw python jsonStr

    let toJson (term: obj) =
        Fable.TypedJson.Json.Encode.toJson python term
