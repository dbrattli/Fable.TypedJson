(**
# JsonSchemaGen — derive a JSON Schema doc from a record type

Walks the F# record's reflection + the user's `CodecRegistry` to emit a
JSON Schema fragment. Equivalent to Pydantic's `model_json_schema()`.

For each field:
- primitive `string` / `int` / `int64` / `float` / `bool` → `{"type": "..."}`
- F# record type → `{"type": "object", ...}` recursively
- F# `'T list` / `'T[]` → `{"type": "array", "items": <inner schema>}`
- F# `'T option` → schema of `'T`, but the field is excluded from `required`
- type registered in the `CodecRegistry` → the codec's own `Schema`

CaseRules is applied to property names so the schema reflects actual JSON
keys (after the same transformation a decode/encode would apply).
*)

module Fable.TypedJson.JsonSchemaGen

open FSharp.Reflection
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

// ---------------------------------------------------------------------------
// Reflection-driven schema generation
// ---------------------------------------------------------------------------

let primitive (typeName: string) : JsonSchemaValue =
    SVDict(Map.ofList [ "type", SVStr typeName ])

/// Resolve the JSON key for a record field, preferring an alias over the
/// case-rule-derived form (matches what the codec's decode/encode does).
/// Aliases are stored canonicalized to PascalCase so the same lookup works
/// regardless of how the backend's reflection spells the field.
let resolveJsonKey (aliases: Map<string, string>) (caseRules: Json.CaseRules) (fieldName: string) : string =
    let canonical = Json.applyCaseRule Json.CaseRules.PascalCase fieldName

    match Map.tryFind canonical aliases with
    | Some alias -> alias
    | None -> Json.applyCaseRule caseRules fieldName

let rec schemaForType (registry: CodecRegistry) (caseRules: Json.CaseRules) (t: System.Type) : JsonSchemaValue =
    let fullName = t.FullName

    // 1. User-registered custom codec wins — its schema was stored at
    //    registration time alongside the boxed decode/encode closures.
    match tryGetCodecEntry fullName registry with
    | Some entry -> SVDict entry.schema
    | None ->
        // 2. Primitive matches.
        match fullName with
        | "System.String" -> primitive "string"
        | "System.Int32"
        | "System.Int64" -> primitive "integer"
        | "System.Double"
        | "System.Single" -> primitive "number"
        | "System.Boolean" -> primitive "boolean"
        | _ ->
            // 3. Option<T> — emit the inner type's schema; "required" is
            //    handled at the parent record level.
            if isOptionType fullName then
                schemaForType registry caseRules (getGenericInnerType t)

            // 4. F# list / .NET array → JSON array.
            elif isFSharpListType fullName || t.IsArray then
                let elementType =
                    if t.IsArray then
                        t.GetElementType()
                    else
                        getGenericInnerType t

                SVDict(Map.ofList [ "type", SVStr "array"; "items", schemaForType registry caseRules elementType ])

            // 5. F# record → recursive object schema (no aliases — only the
            //    top-level codec's aliases are honored; nested records use
            //    plain CaseRules).
            elif FSharpType.IsRecord t then
                schemaForRecord registry Map.empty caseRules t

            else
                // Unknown — fall back to a permissive empty object.
                SVDict emptySchema

and schemaForRecord
    (registry: CodecRegistry)
    (aliases: Map<string, string>)
    (caseRules: Json.CaseRules)
    (recordType: System.Type)
    : JsonSchemaValue =
    let fields = FSharpType.GetRecordFields recordType

    let propertyEntries =
        fields
        |> Array.toList
        |> List.map (fun fi ->
            let key = resolveJsonKey aliases caseRules fi.Name
            let propSchema = schemaForType registry caseRules fi.PropertyType
            key, propSchema)

    let required =
        fields
        |> Array.toList
        |> List.choose (fun fi ->
            if isOptionType fi.PropertyType.FullName then
                None
            else
                Some(SVStr(resolveJsonKey aliases caseRules fi.Name)))

    let baseSchema =
        Map.ofList [
            "type", SVStr "object"
            "title", SVStr recordType.Name
            "properties", SVDict(Map.ofList propertyEntries)
        ]

    let withRequired =
        if List.isEmpty required then
            baseSchema
        else
            Map.add "required" (SVList required) baseSchema

    SVDict withRequired

/// Generate a JSON Schema document for record type `'T`, given a registry of
/// custom codecs and the case rule used for field-name → JSON-key mapping.
/// Returns the schema as a JSON string (via the supplied backend). For alias
/// support, prefer `jsonSchemaOfCodec` which reads aliases off a TypedJson.
let inline jsonSchemaOf<'T> (backend: IJsonBackend) (registry: CodecRegistry) (caseRules: Json.CaseRules) : string =
    let t = typeof<'T>

    let schemaValue = schemaForRecord registry Map.empty caseRules t

    match schemaValue with
    | SVDict m -> schemaToJson backend m
    | _ -> backend.Stringify(toNative backend schemaValue)

/// Generate a JSON Schema document from a `TypedJson<'T>` codec. Honors any
/// aliases attached via `TypedJson.alias`.
let inline jsonSchemaOfCodec<'T>
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (caseRules: Json.CaseRules)
    (codec: Json.TypedJson<'T>)
    : string =
    let t = typeof<'T>

    let schemaValue = schemaForRecord registry codec.aliases caseRules t

    match schemaValue with
    | SVDict m -> schemaToJson backend m
    | _ -> backend.Stringify(toNative backend schemaValue)
