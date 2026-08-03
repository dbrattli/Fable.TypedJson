(**
# JsonSchemaGen — render a type's JSON Schema

Equivalent to Pydantic's `model_json_schema()`.

This module used to carry its own reflection walker — a third traversal of the
type tree alongside decode's and encode's, which had to agree with them by
convention and did not: it emitted `{}` for any tagged DU, and honoured aliases
on the top-level record only. The schema now comes off the same `Plan` node as
`Decode` and `Encode`, so a type's three faces are produced by one walk and
cannot drift.

What is left here is rendering: turning the backend-agnostic `JsonSchemaValue`
tree into a backend-native value and then a string.

adr: schema is data on the plan node, not a separate walk — drift between the three was structural, not accidental
*)

module Fable.TypedJson.JsonSchemaGen

open Fable.TypedJson.Backend
open Fable.TypedJson.Schema

// ---------------------------------------------------------------------------
// JSON Schema → backend-native value conversion
// ---------------------------------------------------------------------------

let rec toNative (backend: IJsonBackend) (v: JsonSchemaValue) : obj =
    match v with
    | SVStr s -> box s
    | SVInt i -> box i
    | SVFloat f -> box f
    | SVBool b -> box b
    | SVList xs -> backend.BuildArray(xs |> List.map (toNative backend))
    | SVDict m ->
        m
        |> Map.fold (fun acc k v -> backend.Put(acc, k, toNative backend v)) (backend.NewMap())

/// Emit a `JsonSchema` (as a `Map<string, JsonSchemaValue>`) into a JSON string
/// via the backend's `Stringify`.
let schemaToJson (backend: IJsonBackend) (schema: JsonSchema) : string =
    backend.Stringify(toNative backend (SVDict schema))

/// Emit a `JsonSchemaValue` — the shape a `Plan` node carries — as JSON.
let schemaValueToJson (backend: IJsonBackend) (v: JsonSchemaValue) : string = backend.Stringify(toNative backend v)

// ---------------------------------------------------------------------------
// Entry points
// ---------------------------------------------------------------------------

/// Non-inline core. Same one-call-inline discipline as `Json.buildCodec`: the
/// inline entry points below exist only to capture `typeof<'T>`, so nothing
/// they touch has to stay public for consumers to inline against.
let schemaJsonFor
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (aliases: Map<string, string>)
    (caseRules: Json.CaseRules)
    (typ: System.Type)
    : string =
    (Plan.forType backend registry (Json.resolveKey aliases caseRules) (Json.applyCaseRule caseRules) typ).Schema
    |> schemaValueToJson backend

/// The schema as the `JsonSchemaValue` tree rather than rendered JSON, for
/// consumers that need to splice it into a larger document — an OpenAPI
/// `components/schemas` map in particular. Re-parsing `schemaJsonFor`'s string
/// to recover this was the alternative.
let schemaValueFor
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (aliases: Map<string, string>)
    (caseRules: Json.CaseRules)
    (typ: System.Type)
    : JsonSchemaValue =
    (Plan.forType backend registry (Json.resolveKey aliases caseRules) (Json.applyCaseRule caseRules) typ).Schema

/// The schema in `$ref` mode: the root's own fragment, plus every record and
/// union reached beneath it as a named definition. `refPrefix` is prepended to
/// each name to form the pointer — `"#/$defs/"` for a standalone JSON Schema
/// document, `"#/components/schemas/"` for OpenAPI.
///
/// Unlike flat mode, a recursive type survives: the cycle guard emits a
/// reference back to the ancestor that registers the body.
let schemaValueWithDefsFor
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (aliases: Map<string, string>)
    (caseRules: Json.CaseRules)
    (refPrefix: string)
    (typ: System.Type)
    : JsonSchemaValue * Map<string, JsonSchemaValue> =
    let plan =
        Plan.forTypeWithRefs backend registry (Json.resolveKey aliases caseRules) (Json.applyCaseRule caseRules) refPrefix typ

    plan.Schema, plan.Definitions

/// Generate a JSON Schema document for type `'T`, given a registry of custom
/// codecs and the case rule mapping F# field names to JSON keys. For alias
/// support prefer `jsonSchemaOfCodec`, which reads both off an existing codec.
let inline jsonSchemaOf<'T> (backend: IJsonBackend) (registry: CodecRegistry) (caseRules: Json.CaseRules) : string =
    schemaJsonFor backend registry Map.empty caseRules typeof<'T>

/// Generate a JSON Schema document from a `TypedJson<'T>` codec, so the schema
/// matches exactly what that codec accepts and produces — same case rule, same
/// aliases, at every depth.
///
/// The standalone walker applied aliases to the top-level record only; sharing
/// the codec's key transform fixes that as a side effect.
let inline jsonSchemaOfCodec<'T> (backend: IJsonBackend) (registry: CodecRegistry) (codec: Json.TypedJson<'T>) : string =
    schemaJsonFor backend registry codec.aliases codec.caseRules typeof<'T>

/// The `JsonSchemaValue` tree for `'T`, for splicing into a larger document.
let inline jsonSchemaValueOf<'T> (backend: IJsonBackend) (registry: CodecRegistry) (caseRules: Json.CaseRules) : JsonSchemaValue =
    schemaValueFor backend registry Map.empty caseRules typeof<'T>

/// The `$ref`-mode schema for `'T`: its own fragment plus the named definitions
/// of every record and union beneath it.
let inline jsonSchemaWithDefsOf<'T>
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (caseRules: Json.CaseRules)
    (refPrefix: string)
    : JsonSchemaValue * Map<string, JsonSchemaValue> =
    schemaValueWithDefsFor backend registry Map.empty caseRules refPrefix typeof<'T>

/// The `$ref`-mode schema for a codec's type, sharing its case rule and aliases
/// at every depth — the same guarantee `jsonSchemaOfCodec` gives.
let inline jsonSchemaWithDefsOfCodec<'T>
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (codec: Json.TypedJson<'T>)
    (refPrefix: string)
    : JsonSchemaValue * Map<string, JsonSchemaValue> =
    schemaValueWithDefsFor backend registry codec.aliases codec.caseRules refPrefix typeof<'T>
