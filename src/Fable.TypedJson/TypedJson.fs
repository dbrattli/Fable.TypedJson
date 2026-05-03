(**
# Fable.TypedJson — JSON Encode/Decode Layer

Thin wrapper over Fable.TypedJson.Schema that adds JSON serialization
via an `IJsonBackend` shim and CaseRules for key transformation.

Schema handles format-agnostic validation and coercion.
TypedJson handles JSON-specific concerns (serialization, casing).

principle: TypedJson is a thin layer -- validation logic lives in Schema
principle: explicit casing at call site, not baked into type definitions
principle: backend-agnostic core; per-backend shim provides Parse/Stringify
adr: CaseRules implemented at runtime since Fable.Core.CaseRules is compile-time only
*)

module Fable.TypedJson.Json

open Fable.Core
open FSharp.Reflection
open Fable.TypedJson.Backend
open Fable.TypedJson.Schema

// ============================================================================
// Case Rules
// ============================================================================

type CaseRules =
    | None = 0
    | LowerFirst = 1
    | SnakeCase = 2
    | SnakeCaseAllCaps = 3
    | KebabCase = 4
    | PascalCase = 5

let private toSnakeCase (name: string) : string =
    let mutable result = ""

    for i = 0 to name.Length - 1 do
        let c = name.[i]

        if System.Char.IsUpper(c) then
            if i > 0 then
                result <- result + "_"

            result <- result + string (System.Char.ToLowerInvariant(c))
        else
            result <- result + string c

    result

let private lowerFirst (name: string) : string =
    if name.Length = 0 then
        name
    else
        string (System.Char.ToLowerInvariant(name.[0]))
        + name.[1..]

let private dashify (separator: string) (name: string) : string =
    let mutable result = ""

    for i = 0 to name.Length - 1 do
        let c = name.[i]

        if System.Char.IsUpper(c) then
            if i > 0 then
                result <- result + separator

            result <- result + string (System.Char.ToLowerInvariant c)
        else
            result <- result + string c

    result

/// Convert a snake_case name back to PascalCase.
let fromSnakeCase (name: string) : string =
    let parts = name.Split('_')

    parts
    |> Array.map (fun part ->
        if part.Length = 0 then
            part
        else
            string (System.Char.ToUpperInvariant(part.[0]))
            + part.[1..])
    |> String.concat ""

/// True if the name contains any uppercase letter (i.e., looks like Pascal/camelCase
/// rather than snake_case). Used to decide whether to convert before applying a rule.
let private hasUpper (name: string) : bool =
    let mutable i = 0
    let mutable found = false

    while i < name.Length && not found do
        if System.Char.IsUpper(name.[i]) then
            found <- true

        i <- i + 1

    found

/// Apply a case rule to a field name.
/// Reflection-supplied names differ across Fable targets — BEAM lowercases
/// (`AirTemperature` → `air_temperature`), Python preserves the F# spelling
/// (`AirTemperature`). The rule normalizes to a canonical PascalCase form
/// internally, then emits the requested casing, so the same rule produces
/// consistent output regardless of source casing.
let applyCaseRule (caseRule: CaseRules) (name: string) : string =
    match caseRule with
    | CaseRules.None -> name
    | _ ->
        // Pascal is our canonical pivot. If the name contains any uppercase
        // letter we treat it as already Pascal/camelCase; otherwise we treat
        // it as snake_case.
        let pascal = if hasUpper name then name else fromSnakeCase name

        match caseRule with
        | CaseRules.SnakeCase -> toSnakeCase pascal
        | CaseRules.LowerFirst -> lowerFirst pascal
        | CaseRules.SnakeCaseAllCaps -> (toSnakeCase pascal).ToUpperInvariant()
        | CaseRules.KebabCase -> dashify "-" pascal
        | CaseRules.PascalCase -> pascal
        | _ -> pascal

// ============================================================================
// Core Types
// ============================================================================

/// Opaque alias for the backend's native parsed-JSON map type.
/// Each `IJsonBackend` interprets it as its own concrete type.
type JsonMap = obj

type TypedJson<'T> = {
    /// Decode using the codec's configured `caseRules`. Use `decodeWith` to
    /// override the rule for a single call.
    decode: JsonMap -> Result<'T, FieldError list>
    /// Encode using the codec's configured `caseRules`. Use `encodeWith` to
    /// override the rule for a single call.
    encode: 'T -> string
    /// Decode with an explicit case rule, overriding the codec's default.
    /// Useful when one codec serves multiple JSON formats.
    decodeWith: CaseRules -> JsonMap -> Result<'T, FieldError list>
    /// Encode with an explicit case rule, overriding the codec's default.
    encodeWith: CaseRules -> 'T -> string
    /// Default case rule applied to F# field names → JSON keys. Defaults to
    /// `LowerFirst` (camelCase) — the most common convention for modern
    /// JSON APIs (JS/TS, REST, OpenAPI). Use `withCaseRules` to change it.
    caseRules: CaseRules
    /// Per-field JSON-key overrides keyed by F# field name (canonicalized to
    /// PascalCase). Consulted before `caseRules`. Use `TypedJson.alias` to
    /// extend.
    aliases: Map<string, string>
    /// Rebuild this codec with a different default case rule. Mirrors
    /// `withAliases`. Used by `withCaseRules` and downstream combinators.
    withCaseRules: CaseRules -> TypedJson<'T>
    /// Rebuild this codec with a different alias map. Used by `alias`.
    withAliases: Map<string, string> -> TypedJson<'T>
}

// ============================================================================
// Applicative Operators
// ============================================================================

let (<!>) (f: 'a -> 'b) (r: Result<'a, FieldError list>) : Result<'b, FieldError list> = Result.map f r

let (<*>) (fR: Result<('a -> 'b), FieldError list>) (xR: Result<'a, FieldError list>) : Result<'b, FieldError list> =
    match fR, xR with
    | Ok f, Ok x -> Ok(f x)
    | Error e1, Error e2 -> Error(e1 @ e2)
    | Error e, _ -> Error e
    | _, Error e -> Error e

// ============================================================================
// Encode Module
// ============================================================================

module Encode =
    let string (s: string) : obj = box s

    let int (n: int) : obj = box n

    let float (f: float) : obj = box f

    let bool (b: bool) : obj = box b

    /// Build a JSON object as a backend-native map.
    let object (backend: IJsonBackend) (fields: (string * obj) list) : obj =
        fields
        |> List.fold (fun acc (k, v) -> backend.Put(acc, k, v)) (backend.NewMap())
        |> box

    /// Encode a list of values as a JSON array. The backend converts the
    /// F# list into its native sequence form (Erlang list, Python list, ...)
    /// so subsequent `Stringify` emits a real JSON array.
    let list (backend: IJsonBackend) (encoder: 'T -> obj) (items: 'T list) : obj =
        items |> List.map encoder |> backend.BuildArray

    /// Encode an optional value. `None` becomes the backend's JSON null
    /// representation (e.g., Python `None`, BEAM `null` atom — NOT F# `null`,
    /// which would serialize as the JSON string `"undefined"` on BEAM).
    let optional (backend: IJsonBackend) (encoder: 'T -> obj) (v: 'T option) : obj =
        match v with
        | Some x -> encoder x
        | Option.None -> backend.Null

    /// Parse a JSON string into the backend's native map for embedding as raw JSON.
    let raw (backend: IJsonBackend) (jsonStr: string) : obj = box (backend.ParseRaw jsonStr)

    /// Serialize a backend-native value (map, list, primitive) to a JSON string.
    let toJson (backend: IJsonBackend) (term: obj) : string = backend.Stringify(unbox term)

// ============================================================================
// TypedJson.auto — Thin wrapper over Schema
// ============================================================================

(**
## auto

Creates a TypedJson codec by wrapping Schema.auto with:
- CaseRules key transformation on decode (JSON key → schema field name)
- CaseRules key transformation on encode (field name → JSON key)
- backend-provided Stringify on encode

adr: inline required for typeof<'T> resolution on Fable backends
*)

let inline autoWith<'T> (backend: IJsonBackend) (registry: CodecRegistry) : TypedJson<'T> =
    let typ = typeof<'T>

    // Records have a stable list of fields known at codec-construction time;
    // unions don't (the case is value-dependent). Compute the record fields
    // lazily so this codec works for top-level union types too.
    let fields =
        if FSharpType.IsRecord typ then
            FSharpType.GetRecordFields typ
        else
            [||]

    /// Resolve the JSON key for a given F# field name: alias if present,
    /// otherwise the case-rule-derived form. Lookup is keyed by the field's
    /// PascalCase form so the same alias works regardless of how the
    /// backend's reflection presents the name (BEAM lowercases, Python
    /// preserves the F# spelling).
    let resolveKey (aliases: Map<string, string>) (caseRules: CaseRules) (fieldName: string) : string =
        let canonical = applyCaseRule CaseRules.PascalCase fieldName

        match Map.tryFind canonical aliases with
        | Some alias -> alias
        | None -> applyCaseRule caseRules fieldName

    // Recursive constructor — building a new codec with different aliases or
    // a different default case rule is a single recursive call away. F#
    // allows `let rec` for nested values that capture each other.
    let rec build (aliases: Map<string, string>) (caseRules: CaseRules) : TypedJson<'T> =
        // Build the Schema function ONCE for the codec's default rules+aliases
        // combo. The schema's per-field metadata (jsonKeys, isOpts, innerTypes)
        // is pre-baked inside Schema.auto's inline body and captured here, so
        // the per-decode hot path skips the per-field reflection that the old
        // build-per-call path used to pay.
        let defaultKeyTransform = resolveKey aliases caseRules

        let defaultSchemaFn =
            Fable.TypedJson.Schema.auto<'T> backend registry defaultKeyTransform

        let decodeWith (rules: CaseRules) (map: JsonMap) : Result<'T, FieldError list> =
            // Fast path: caller didn't override the case rule, reuse the
            // cached schema. The fall-through rebuilds for the rare case
            // where decodeWith is invoked with a different rules value.
            let schemaFn =
                if rules = caseRules then
                    defaultSchemaFn
                else
                    let keyTransform = resolveKey aliases rules
                    Fable.TypedJson.Schema.auto<'T> backend registry keyTransform

            // Schema.resolveField applies `keyTransform` itself before looking
            // up; passing a raw adapter avoids double-transforming the key.
            schemaFn (jsonMapAdapter backend map)

        // Recursively transform a value into a backend-native form so that
        // any nested records have their keys mapped through the same
        // CaseRules/aliases the outer record uses. Without this, nested F#
        // records would be Stringify'd by the backend using its default
        // reflection casing (Python: PascalCase, BEAM: lowercase) and leak
        // through to the JSON output. Lists/arrays of records are walked
        // element-wise and rebuilt as backend-native arrays. Primitives,
        // already-encoded option payloads, and unknown types pass through.
        let rec transformValue (rules: CaseRules) (t: System.Type) (v: obj) : obj =
            if isNull v then
                v
            elif isOptionType t.FullName then
                match extractOption v with
                | Some inner -> transformValue rules (getGenericInnerType t) inner
                | None -> backend.Null
            elif isFSharpListType t.FullName then
                let elementType = getGenericInnerType t
                let xs = extractList v

                xs
                |> List.map (fun item -> transformValue rules elementType item)
                |> backend.BuildArray
            elif t.IsArray then
                let elementType = t.GetElementType()
                let arr = unbox<obj[]> v

                arr
                |> Array.toList
                |> List.map (fun item -> transformValue rules elementType item)
                |> backend.BuildArray
            elif FSharpType.IsRecord t then
                // Use FSharpValue.GetRecordField (per-field) rather than
                // FSharpValue.GetRecordFields (whole record). The latter
                // requires recovering the F# record type from a boxed obj
                // and Fable BEAM can't — it lowers `box record`'s type to
                // `System.Object` at runtime, which has no `fields` key.
                // Reading per-field uses the PropertyInfo we already have
                // from the static `t`, so it works on every backend.
                let innerFields = FSharpType.GetRecordFields t

                let entries =
                    innerFields
                    |> Array.toList
                    |> List.choose (fun fi ->
                        let fv = FSharpValue.GetRecordField(v, fi)
                        let jsonKey = resolveKey aliases rules fi.Name

                        if isOptionType fi.PropertyType.FullName then
                            match extractOption fv with
                            | Some inner -> Some(jsonKey, transformValue rules (getGenericInnerType fi.PropertyType) inner)
                            | None -> None
                        else
                            Some(jsonKey, transformValue rules fi.PropertyType fv))

                Encode.object backend entries

            elif FSharpType.IsUnion t then
                // Tagged DU: emit `{type: "<caseName>", ...payload}`.
                // For a fieldless case, just `{type: "..."}`. For a single
                // record-field case, flatten the record's keys alongside the
                // discriminator. Other shapes are unsupported in v1 (matches
                // the decode side in Schema.coerceUnion).
                let caseInfo, caseValues = FSharpValue.GetUnionFields(v, t)
                let tag = tagOfCaseName caseInfo.Name
                let caseFields = caseInfo.GetFields()
                let baseEntry = (discriminatorKey, box tag)

                // `caseValues : obj[]` has a different runtime shape on each
                // Fable backend (process-dict ref on BEAM, GenericArray on
                // Python, native array on .NET). Going through `backend.ArrayAt`
                // / `ArrayLength` gives uniform access.
                let payloadCount = backend.ArrayLength(box caseValues)

                let payload =
                    if payloadCount > 0 then
                        Some(backend.ArrayAt(box caseValues, 0))
                    else
                        None

                match caseFields.Length, payload with
                | 0, _
                | _, None -> Encode.object backend [ baseEntry ]
                | 1, Some payloadValue ->
                    let payloadType = caseFields.[0].PropertyType

                    if FSharpType.IsRecord payloadType then
                        // Walk the payload's fields the same way we walk a
                        // top-level record, so caseRules + aliases apply.
                        let payloadFields = FSharpType.GetRecordFields payloadType

                        let payloadEntries =
                            payloadFields
                            |> Array.toList
                            |> List.choose (fun fi ->
                                let fv = FSharpValue.GetRecordField(payloadValue, fi)
                                let jsonKey = resolveKey aliases rules fi.Name

                                if isOptionType fi.PropertyType.FullName then
                                    match extractOption fv with
                                    | Some inner -> Some(jsonKey, transformValue rules (getGenericInnerType fi.PropertyType) inner)
                                    | None -> None
                                else
                                    Some(jsonKey, transformValue rules fi.PropertyType fv))

                        Encode.object backend (baseEntry :: payloadEntries)
                    else
                        // Non-record single-field cases not supported in v1.
                        Encode.object backend [ baseEntry ]
                | _ ->
                    // Multi-positional-field cases not supported in v1.
                    Encode.object backend [ baseEntry ]
            else
                v

        // Pre-bake the per-field metadata for the default rules so the hot
        // encode path skips per-call `resolveKey`, `isOptionType`, and
        // `getGenericInnerType`. Mirrors the decode-side schema cache.
        let n = fields.Length

        let defaultJsonKeys =
            if FSharpType.IsRecord typ then
                Array.init n (fun i -> resolveKey aliases caseRules fields.[i].Name)
            else
                [||]

        let defaultIsOpts =
            if FSharpType.IsRecord typ then
                Array.init n (fun i -> isOptionType fields.[i].PropertyType.FullName)
            else
                [||]

        let defaultInnerTypes =
            if FSharpType.IsRecord typ then
                Array.init n (fun i ->
                    if defaultIsOpts.[i] then
                        getGenericInnerType fields.[i].PropertyType
                    else
                        fields.[i].PropertyType)
            else
                [||]

        // Pre-bake per-field transformers. `transformValue` dispatches on
        // `System.Type` characteristics (FullName starts-with checks +
        // FSharpType.IsRecord/IsUnion + IsArray), and that dispatch is
        // identical on every call for a given field type. Resolve it once
        // per field and capture the appropriate closure: `id` for primitives
        // (the `else v` fall-through path), specialised closures for option
        // / list / array / record / union. The dispatch happens on
        // `System.Type` (not on `JsonValue`) so the Fable codegen is well
        // exercised — no risk of the erased-DU pattern issue we saw earlier.
        let buildTransformer (rules: CaseRules) (innerType: System.Type) : obj -> obj =
            let fn = innerType.FullName

            if isOptionType fn then
                let valueType = getGenericInnerType innerType

                fun v ->
                    match extractOption v with
                    | Some inner -> transformValue rules valueType inner
                    | Option.None -> backend.Null
            elif isFSharpListType fn then
                let elementType = getGenericInnerType innerType

                fun v ->
                    extractList v
                    |> List.map (fun item -> transformValue rules elementType item)
                    |> backend.BuildArray
            elif innerType.IsArray then
                let elementType = innerType.GetElementType()

                fun v ->
                    unbox<obj[]> v
                    |> Array.toList
                    |> List.map (fun item -> transformValue rules elementType item)
                    |> backend.BuildArray
            elif FSharpType.IsRecord innerType then
                fun v -> transformValue rules innerType v
            elif FSharpType.IsUnion innerType then
                fun v -> transformValue rules innerType v
            else
                // Primitive (string / int / float / bool / ...): identity —
                // matches `transformValue`'s `else v` branch.
                id

        // Build transformers against the INNER type (post-option-unwrap)
        // because `encodeRecordWith` already unwraps the option at the
        // call site and passes the inner value to `transformers.[i]`.
        let defaultTransformers =
            if FSharpType.IsRecord typ then
                Array.init n (fun i -> buildTransformer caseRules defaultInnerTypes.[i])
            else
                [||]

        // Walk a record's fields and write directly into a backend-native
        // map via `Put`. Avoids the `(string * obj) list` cons-cell chain
        // that `Encode.object` builds, and uses pre-baked per-field
        // transformers so each field's `transformValue` dispatch happens
        // once at codec-construction (not per encode).
        let inline encodeRecordWith (jsonKeys: string[]) (isOpts: bool[]) (transformers: (obj -> obj)[]) (record: 'T) : obj =
            let mutable acc = backend.NewMap()
            let boxed = box record

            for i = 0 to n - 1 do
                let v = FSharpValue.GetRecordField(boxed, fields.[i])

                if isOpts.[i] then
                    match extractOption v with
                    | Some inner -> acc <- backend.Put(acc, jsonKeys.[i], transformers.[i] inner)
                    | Option.None -> ()
                else
                    acc <- backend.Put(acc, jsonKeys.[i], transformers.[i] v)

            acc

        let encodeWith (rules: CaseRules) (record: 'T) : string =
            // Top-level dispatch mirrors decode (Schema.auto): record →
            // field-by-field encode; union → discriminator + payload via
            // transformValue. Anything else: hand off to the backend's
            // Stringify and hope for the best.
            if FSharpType.IsRecord typ then
                let map =
                    if rules = caseRules then
                        // Hot path: reuse the pre-baked metadata + transformers.
                        encodeRecordWith defaultJsonKeys defaultIsOpts defaultTransformers record
                    else
                        // Cold path: caller overrode the rule, recompute keys
                        // and rebuild transformers (rules only enters the
                        // transformers via the recursive `transformValue`
                        // calls they make on nested records / lists).
                        let jsonKeys =
                            Array.init n (fun i -> resolveKey aliases rules fields.[i].Name)

                        let transformers =
                            Array.init n (fun i -> buildTransformer rules defaultInnerTypes.[i])

                        encodeRecordWith jsonKeys defaultIsOpts transformers record

                map |> Encode.toJson backend
            elif FSharpType.IsUnion typ then
                transformValue rules typ (box record)
                |> Encode.toJson backend
            else
                Encode.toJson backend (box record)

        {
            decode = decodeWith caseRules
            encode = encodeWith caseRules
            decodeWith = decodeWith
            encodeWith = encodeWith
            caseRules = caseRules
            aliases = aliases
            withCaseRules = fun newRules -> build aliases newRules
            withAliases = fun newAliases -> build newAliases caseRules
        }

    build Map.empty CaseRules.LowerFirst

/// Auto codec with the default empty codec registry. Use `autoWith` to pass a registry of custom codecs.
let inline auto<'T> (backend: IJsonBackend) : TypedJson<'T> =
    autoWith<'T> backend Fable.TypedJson.Schema.emptyRegistry

/// Shorthand: create auto codec and decode in one call (uses the codec's default `LowerFirst`).
let inline validate<'T> (backend: IJsonBackend) (map: JsonMap) : Result<'T, FieldError list> = (auto<'T> backend).decode map

/// Shorthand: create autoWith codec (with custom registry) and decode in one call.
let inline validateWith<'T> (backend: IJsonBackend) (registry: CodecRegistry) (map: JsonMap) : Result<'T, FieldError list> =
    (autoWith<'T> backend registry).decode map

/// Compose a cross-field model validator onto a codec. Runs after the per-field
/// decode succeeds. If the validator returns Error, those errors replace the success.
///
///   let codec =
///       auto<EventRange> beam
///       |> withModel (fun r ->
///           if r.Start <= r.Until then Ok r
///           else Error [{ path = ""; message = "Start must precede Until" }])
///
/// Pydantic equivalent: `@model_validator(mode="after")`.
let withModel (validator: 'T -> Result<'T, FieldError list>) (codec: TypedJson<'T>) : TypedJson<'T> =
    // Wrap the inner codec's decode with the model validator. Rebuild
    // recursively so subsequent `alias` / `withCaseRules` calls re-apply
    // the validator on the fresh inner codec.
    let rec wrap (inner: TypedJson<'T>) : TypedJson<'T> =
        let decodeWith (rules: CaseRules) (map: JsonMap) =
            match inner.decodeWith rules map with
            | Ok v -> validator v
            | Error errs -> Error errs

        {
            decode = decodeWith inner.caseRules
            encode = inner.encode
            decodeWith = decodeWith
            encodeWith = inner.encodeWith
            caseRules = inner.caseRules
            aliases = inner.aliases
            withCaseRules = fun rules -> wrap (inner.withCaseRules rules)
            withAliases = fun aliases -> wrap (inner.withAliases aliases)
        }

    wrap codec

/// Set the default case rule used to derive JSON keys from F# field names.
/// Defaults to `LowerFirst` (camelCase). Set to `SnakeCase` for snake_case
/// APIs, `KebabCase` for hyphen-separated keys, etc.
///
///   let codec = auto<WeatherRequest> beam |> withCaseRules CaseRules.SnakeCase
///
/// Per-field overrides via `alias` still take precedence.
let withCaseRules (caseRules: CaseRules) (codec: TypedJson<'T>) : TypedJson<'T> = codec.withCaseRules caseRules

/// Override the JSON key for a single F# record field, replacing whatever
/// the active `CaseRules` would have produced. Affects decode lookup,
/// encode output, and the `jsonSchemaOf` property name.
///
///   type WeatherRequest = { Location: string; Days: int }
///   let codec =
///       auto<WeatherRequest> beam
///       |> alias "Location" "loc"
///       |> alias "Days" "n"
///
/// The `fieldName` is normalized to PascalCase internally so the same call
/// works regardless of how each backend's reflection presents the field.
///
/// Pydantic equivalent: `Field(alias="loc")`.
let alias (fieldName: string) (jsonKey: string) (codec: TypedJson<'T>) : TypedJson<'T> =
    let canonical = applyCaseRule CaseRules.PascalCase fieldName
    codec.withAliases (Map.add canonical jsonKey codec.aliases)
