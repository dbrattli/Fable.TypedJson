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

let inline validateJson<'T> (map: obj) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateJson<'T> dotnet map

let inline validateMap<'T> (map: Map<string, string>) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateMap<'T> dotnet map

// Registry-aware shortcuts support refined and custom-codec fields without
// requiring callers to build a reusable `TypedJson` value first.
let inline validateJsonWith<'T> (registry: CodecRegistry) (map: obj) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateJsonWith<'T> dotnet registry map

let inline validateMapWith<'T> (registry: CodecRegistry) (map: Map<string, string>) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateMapWith<'T> dotnet registry map

/// Validate a `Map<string, string>` whose keys are in `caseRules` spelling —
/// snake_case LLM tool-call arguments in particular. Matching is strict:
/// `SnakeCase` reads `device_id`, not `deviceId`. For aliases or a registry,
/// use a codec's `decodeStringMap`.
let inline validateMapWithCaseRules<'T> (caseRules: CaseRules) (map: Map<string, string>) : Result<'T, FieldError list> =
    Fable.TypedJson.Json.validateMapWithCaseRules<'T> dotnet caseRules map

let inline dumpWith<'T> (registry: CodecRegistry) (record: 'T) : obj =
    Fable.TypedJson.Json.dumpWith<'T> dotnet registry record

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

/// The schema as a `JsonSchemaValue` tree rather than rendered JSON, for
/// splicing into a larger document — an OpenAPI `components/schemas` map in
/// particular.
let inline jsonSchemaValueOf<'T> (registry: CodecRegistry) (caseRules: CaseRules) : JsonSchemaValue =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaValueOf<'T> dotnet registry caseRules

/// The `$ref`-mode schema for `'T`: its own fragment, plus every record and
/// union beneath it as a named definition. `refPrefix` forms the pointer —
/// `"#/$defs/"` for a standalone document, `"#/components/schemas/"` for OpenAPI.
/// Unlike flat mode, a recursive type round-trips instead of truncating.
let inline jsonSchemaWithDefsOf<'T>
    (registry: CodecRegistry)
    (caseRules: CaseRules)
    (refPrefix: string)
    : JsonSchemaValue * Map<string, JsonSchemaValue> =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaWithDefsOf<'T> dotnet registry caseRules refPrefix

/// The `$ref`-mode schema for a codec's type, sharing its case rule and aliases
/// at every depth.
let inline jsonSchemaWithDefsOfCodec<'T>
    (registry: CodecRegistry)
    (codec: TypedJson<'T>)
    (refPrefix: string)
    : JsonSchemaValue * Map<string, JsonSchemaValue> =
    Fable.TypedJson.JsonSchemaGen.jsonSchemaWithDefsOfCodec<'T> dotnet registry codec refPrefix

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
