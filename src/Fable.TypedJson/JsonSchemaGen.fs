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

(**
## Cycle guard

A self-referential record (`type Tree = { Children: Tree list }`) would otherwise
recurse forever here — the emitter has no natural base case, because a record's
schema is defined in terms of its fields' schemas. `visited` carries the chain of
record `FullName`s on the path from the root to the current node; re-entering a
record already on that chain emits a truncated `{"type": "object", "title": ...}`
instead of descending again.

On BEAM this was previously masked: `typeof<Tree>` crashed the compiler before
reflection could reach the emitter (fable-compiler/Fable#4766). It has always
been reachable on .NET, Python and JS.

invariant: `visited` is the root-to-node path, not a global accumulator — sibling fields of the same type each expand fully
adr: truncate to a title-only object rather than emit `$ref`/`$defs` — LLM tool-call validators handle flat schemas far better
adr: `string list` over `Set` — the chain is nesting-depth short, and `List` is the best-supported collection on all four targets
tradeoff: a recursive type's schema is lossy past the first cycle; the alternative is a schema many consumers reject
assumption: `t.FullName` distinguishes record types on every backend (same key `CodecRegistry` already dispatches on)
*)
let rec private schemaForTypeIn
    (visited: string list)
    (registry: CodecRegistry)
    (caseRules: Json.CaseRules)
    (t: System.Type)
    : JsonSchemaValue =
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
                schemaForTypeIn visited registry caseRules (getGenericInnerType t)

            // 4. F# list / .NET array → JSON array.
            elif isFSharpListType fullName || t.IsArray then
                let elementType =
                    if t.IsArray then
                        t.GetElementType()
                    else
                        getGenericInnerType t

                SVDict(
                    Map.ofList [
                        "type", SVStr "array"
                        "items", schemaForTypeIn visited registry caseRules elementType
                    ]
                )

            // 5. F# record → recursive object schema (no aliases — only the
            //    top-level codec's aliases are honored; nested records use
            //    plain CaseRules). A record already on the path back to the
            //    root is truncated rather than re-expanded — see the cycle
            //    guard note above.
            elif FSharpType.IsRecord t then
                if List.contains fullName visited then
                    SVDict(Map.ofList [ "type", SVStr "object"; "title", SVStr t.Name ])
                else
                    SVDict(schemaForRecordIn visited registry Map.empty caseRules t)

            else
                // Unknown — fall back to a permissive empty object.
                SVDict emptySchema

and private schemaForRecordIn
    (visited: string list)
    (registry: CodecRegistry)
    (aliases: Map<string, string>)
    (caseRules: Json.CaseRules)
    (recordType: System.Type)
    : JsonSchema =
    // The record joins the path before its fields are walked, so a field
    // referring straight back to it truncates instead of descending.
    let visited = recordType.FullName :: visited

    // Walk the fields once, computing the JSON key + required-ness + schema
    // per field. Avoids resolving the key twice (once for properties, once
    // for required) and a redundant Array.toList copy per pass.
    let entries =
        recordType
        |> FSharpType.GetRecordFields
        |> Array.map (fun fi ->
            let key = resolveJsonKey aliases caseRules fi.Name
            let isOpt = isOptionType fi.PropertyType.FullName
            let propSchema = schemaForTypeIn visited registry caseRules fi.PropertyType
            key, isOpt, propSchema)

    let propertyEntries =
        entries
        |> Array.map (fun (k, _, s) -> k, s)
        |> Array.toList

    let required =
        entries
        |> Array.choose (fun (k, isOpt, _) -> if isOpt then None else Some(SVStr k))
        |> Array.toList

    let baseSchema =
        Map.ofList [
            "type", SVStr "object"
            "title", SVStr recordType.Name
            "properties", SVDict(Map.ofList propertyEntries)
        ]

    if List.isEmpty required then
        baseSchema
    else
        Map.add "required" (SVList required) baseSchema

/// Emit the JSON Schema fragment for an arbitrary type. Entry point for the
/// recursive emitter — starts with an empty cycle-guard path.
let schemaForType (registry: CodecRegistry) (caseRules: Json.CaseRules) (t: System.Type) : JsonSchemaValue =
    schemaForTypeIn [] registry caseRules t

/// Emit the JSON Schema object for a record type. Entry point for the
/// recursive emitter — starts with an empty cycle-guard path.
let schemaForRecord
    (registry: CodecRegistry)
    (aliases: Map<string, string>)
    (caseRules: Json.CaseRules)
    (recordType: System.Type)
    : JsonSchema =
    schemaForRecordIn [] registry aliases caseRules recordType

/// Generate a JSON Schema document for record type `'T`, given a registry of
/// custom codecs and the case rule used for field-name → JSON-key mapping.
/// Returns the schema as a JSON string (via the supplied backend). For alias
/// support, prefer `jsonSchemaOfCodec` which reads aliases off a TypedJson.
let inline jsonSchemaOf<'T> (backend: IJsonBackend) (registry: CodecRegistry) (caseRules: Json.CaseRules) : string =
    schemaForRecord registry Map.empty caseRules typeof<'T>
    |> schemaToJson backend

/// Generate a JSON Schema document from a `TypedJson<'T>` codec. Reads the
/// codec's configured `caseRules` and any `alias`-attached overrides so the
/// emitted schema matches what the codec actually accepts and produces.
let inline jsonSchemaOfCodec<'T> (backend: IJsonBackend) (registry: CodecRegistry) (codec: Json.TypedJson<'T>) : string =
    schemaForRecord registry codec.aliases codec.caseRules typeof<'T>
    |> schemaToJson backend
