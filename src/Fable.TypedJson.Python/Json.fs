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

let inline validateJson<'T> (map: obj) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateJson<'T> python map

let inline validateMap<'T> (map: Map<string, string>) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateMap<'T> python map

// The `…With` variants take a registry, so a record with refined or
// custom-codec fields can be validated without building a codec. Their
// absence here was a real gap: the README documented `validateMap` for exactly
// such a record, and it could not have worked — `validateMap` uses an empty
// registry, so a `NonEmptyString` field had no codec to dispatch through.
let inline validateJsonWith<'T> (registry: CodecRegistry) (map: obj) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateJsonWith<'T> python registry map

let inline validateMapWith<'T> (registry: CodecRegistry) (map: Map<string, string>) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateMapWith<'T> python registry map

/// Validate a `Map<string, string>` whose keys are in `caseRules` spelling —
/// snake_case LLM tool-call arguments in particular. Matching is strict:
/// `SnakeCase` reads `device_id`, not `deviceId`. For aliases or a registry,
/// use a codec's `decodeStringMap`.
let inline validateMapWithCaseRules<'T> (caseRules: CaseRules) (map: Map<string, string>) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateMapWithCaseRules<'T> python caseRules map

let inline dumpWith<'T> (registry: CodecRegistry) (record: 'T) : obj =
    Fable.TypedJson.Json.dumpWith<'T> python registry record

let inline dump<'T> (record: 'T) : obj =
    Fable.TypedJson.Json.dump<'T> python record

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

/// The schema as a `JsonSchemaValue` tree rather than rendered JSON, for
/// splicing into a larger document — an OpenAPI `components/schemas` map in
/// particular.
let inline jsonSchemaValueOf<'T> (registry: CodecRegistry) (caseRules: CaseRules) : JsonSchemaValue =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaValueOf<'T> python registry caseRules

/// The `$ref`-mode schema for `'T`: its own fragment, plus every record and
/// union beneath it as a named definition. `refPrefix` forms the pointer —
/// `"#/$defs/"` for a standalone document, `"#/components/schemas/"` for OpenAPI.
/// Unlike flat mode, a recursive type round-trips instead of truncating.
let inline jsonSchemaWithDefsOf<'T>
    (registry: CodecRegistry)
    (caseRules: CaseRules)
    (refPrefix: string)
    : JsonSchemaValue * Map<string, JsonSchemaValue> =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaWithDefsOf<'T> python registry caseRules refPrefix

/// The `$ref`-mode schema for a codec's type, sharing its case rule and aliases
/// at every depth.
let inline jsonSchemaWithDefsOfCodec<'T>
    (registry: CodecRegistry)
    (codec: TypedJson<'T>)
    (refPrefix: string)
    : JsonSchemaValue * Map<string, JsonSchemaValue> =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaWithDefsOfCodec<'T> python registry codec refPrefix

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
