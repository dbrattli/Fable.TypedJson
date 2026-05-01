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
    let schemaFn = Fable.TypedJson.Schema.auto<'T> backend registry
    let typ = typeof<'T>
    let fields = FSharpType.GetRecordFields typ

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
        let decodeWith (rules: CaseRules) (map: JsonMap) : Result<'T, FieldError list> =
            let lookup (fieldName: string) : JsonValue option =
                let jsonKey = resolveKey aliases rules fieldName
                jsonMapAdapter backend map jsonKey

            schemaFn lookup

        let encodeWith (rules: CaseRules) (record: 'T) : string =
            let values = FSharpValue.GetRecordFields(box record)

            let entries =
                Array.zip fields values
                |> Array.toList
                |> List.choose (fun (fi, v) ->
                    let jsonKey = resolveKey aliases rules fi.Name

                    if isOptionType fi.PropertyType.FullName then
                        match unbox<obj option> v with
                        | Some inner -> Some(jsonKey, inner)
                        | None -> None
                    else
                        Some(jsonKey, v))

            Encode.object backend entries |> Encode.toJson backend

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
let inline validate<'T> (backend: IJsonBackend) (map: JsonMap) : Result<'T, FieldError list> =
    (auto<'T> backend).decode map

/// Shorthand: create autoWith codec (with custom registry) and decode in one call.
let inline validateWith<'T>
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (map: JsonMap)
    : Result<'T, FieldError list> =
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
let withCaseRules (caseRules: CaseRules) (codec: TypedJson<'T>) : TypedJson<'T> =
    codec.withCaseRules caseRules

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
