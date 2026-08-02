(**
# Fable.TypedJson — the public codec API

`CaseRules` and aliases (how an F# field name becomes a JSON key), the
`TypedJson<'T>` codec record, and the combinators that rebuild one. The work
of walking a type belongs to `Plan`; this module decides what the keys are and
hands the walker a transform.

principle: TypedJson is a thin layer — walking a type lives in Plan, the vocabulary in Schema
principle: explicit casing at call site, not baked into type definitions
principle: backend-agnostic core; per-backend shim provides Parse/Stringify
adr: CaseRules implemented at runtime since Fable.Core.CaseRules is compile-time only
adr: one plan per (type, case rule, aliases), built at codec construction and captured — build a codec once and reuse it
invariant: `decode`, `encode` and the emitted JSON Schema all read the same plan, so they cannot disagree about a wire shape
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

/// Insert `separator` before each uppercase letter (except at index 0) and
/// lowercase the letter. Powers both `toSnakeCase` (separator = "_") and
/// `dashify` (separator = "-"). The body is imperative — `Seq.mapi` over a
/// string fails with `badarg` on Fable BEAM (its seq enumerator doesn't
/// drive the binary correctly), and an indexed `for` loop with
/// `result + …` lowers cleanly to binary append on every backend.
/// Runs at codec-construction time, not on the hot path.
let private separateUpper (separator: string) (name: string) : string =
    let mutable result = ""

    for i = 0 to name.Length - 1 do
        let c = name.[i]

        if System.Char.IsUpper c then
            if i > 0 then
                result <- result + separator

            result <- result + string (System.Char.ToLowerInvariant c)
        else
            result <- result + string c

    result

let private toSnakeCase (name: string) : string = separateUpper "_" name

let private dashify (separator: string) (name: string) : string = separateUpper separator name

/// Convert a snake_case name back to PascalCase.
/// Re-exported from `Casing` so `Schema` can share the pivot — `Schema`
/// compiles first, and this name is public API.
let fromSnakeCase (name: string) : string = Casing.fromSnakeCase name

/// Apply a case rule to a field name.
/// Reflection reports the F# spelling (`AirTemperature`) on every target, but
/// the rule still normalizes to a canonical PascalCase form before emitting the
/// requested casing — so it produces the same output whether it is handed an F#
/// field name, a snake_case name, or a name already in the target casing.
/// BEAM reflection reported snake_case before Fable 5.8.1
/// (fable-compiler/Fable#4766); the normalization keeps that input working too.
let applyCaseRule (caseRule: CaseRules) (name: string) : string =
    match caseRule with
    | CaseRules.None -> name
    | _ ->
        // Pascal is our canonical pivot — see `Casing`. Shared with
        // `Schema.lowerFirstTransform` so both key paths agree.
        let pascal = Casing.toCanonicalPascal name

        match caseRule with
        | CaseRules.SnakeCase -> toSnakeCase pascal
        | CaseRules.LowerFirst -> Casing.lowerFirst pascal
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

(**
The key and tag transforms the registry-free shortcut entry points
(`validateMap`, `validateJson`, `dump`) use: camelCase, no aliases.

Byte-identical to what `Schema.lowerFirstTransform` produced, and to the
codec's own `LowerFirst` default — which is what let the shortcut flow move
onto the codec's walker without a wire-format change.

invariant: the shortcut entry points and a default-configured codec derive the same key from the same field
*)
let camelCaseKey: string -> string = resolveKey Map.empty CaseRules.LowerFirst

let camelCaseTag: string -> string = applyCaseRule CaseRules.LowerFirst

(**
Non-inline core of `autoWith`. Takes the `System.Type` explicitly, so the
inline entry point below is exactly one call.

That shape is load-bearing for the public surface, not a style choice.
Everything an `inline` body touches must be accessible at the consumer's call
site, in the consumer's assembly — which is why a substantial inline body
forced `Plan`, the walker helpers and the reflection primitives to all be
public. Keep every public `inline` function to a single call into a non-inline
one and the internals can be hidden.

`'T` here is for typing only; the runtime shape comes from `typ`. Fable erases
it, so the `unbox` inside costs nothing.

invariant: the body of every public `inline` function is exactly one call to a non-inline function taking System.Type
*)
let buildCodec<'T> (backend: IJsonBackend) (registry: CodecRegistry) (typ: System.Type) : TypedJson<'T> =

    // Recursive constructor — building a new codec with different aliases or
    // a different default case rule is a single recursive call away. F#
    // allows `let rec` for nested values that capture each other.
    let rec build (aliases: Map<string, string>) (caseRules: CaseRules) : TypedJson<'T> =
        // Build the plan ONCE for the codec's default rules+aliases combo, and
        // capture it, so the per-call hot path skips the per-field reflection the old
        // build-per-call path used to pay.
        let defaultKeyTransform = resolveKey aliases caseRules
        // Tag transform applies the case rule directly to the F# union case
        // name (no aliasing — case names don't have an alias mechanism today).
        // Encode side mirrors via `applyCaseRule rules caseInfo.Name`.
        let defaultTagTransform = applyCaseRule caseRules

        // Resolve the whole type tree once, here. `Schema.auto` pre-baked only
        // the top-level record and left nested records to re-reflect on every
        // decode; the plan walks to the leaves, so a nested record now costs
        // the same per decode as a top-level one.
        let defaultPlan =
            Plan.forType backend registry defaultKeyTransform defaultTagTransform typ

        let decodeWith (rules: CaseRules) (map: JsonMap) : Result<'T, FieldError list> =
            // Fast path: caller didn't override the case rule, reuse the plan.
            // The fall-through rebuilds for the rare case where decodeWith is
            // invoked with a different rules value.
            let plan =
                if rules = caseRules then
                    defaultPlan
                else
                    Plan.forType backend registry (resolveKey aliases rules) (applyCaseRule rules) typ

            plan.Decode map |> Result.map unbox<'T>

        // Encode reads the same plan decode does, so the two cannot disagree
        // about a type's wire shape. The old encode side kept its own parallel
        // pre-bake (`defaultJsonKeys` / `defaultIsOpts` / `defaultTransformers`)
        // for the top level and re-reflected below it, mirroring the decode
        // side's split; one plan replaces both halves of both.
        let encodeWith (rules: CaseRules) (record: 'T) : string =
            let plan =
                if rules = caseRules then
                    defaultPlan
                else
                    Plan.forType backend registry (resolveKey aliases rules) (applyCaseRule rules) typ

            plan.Encode(box record) |> Encode.toJson backend

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

let inline autoWith<'T> (backend: IJsonBackend) (registry: CodecRegistry) : TypedJson<'T> =
    buildCodec<'T> backend registry typeof<'T>

/// Auto codec with the default empty codec registry. Use `autoWith` to pass a registry of custom codecs.
let inline auto<'T> (backend: IJsonBackend) : TypedJson<'T> =
    autoWith<'T> backend Fable.TypedJson.Schema.emptyRegistry

(**
Dump a record to a backend-native JSON map (e.g. for inter-process messaging).

Routed through the same plan the codec's encode path uses, rather than a
separate top-level-only fold. Two things follow: nested records, lists and
options are transformed instead of being written verbatim, and the key
derivation is shared instead of duplicated — `resolveKey Map.empty
CaseRules.LowerFirst` produces exactly the camelCase key
`Schema.lowerFirstTransform` does, which is what `validateJson` reads back.

The non-recursive version emitted a nested record in whatever spelling the
backend's own reflection used, so `validateJson` could not find its keys and
the round-trip invariant held only for flat records.

Note this builds a plan per call. Prefer a codec built once (`auto<'T> ()`)
for anything repeated; `dump` is a convenience for one-off inter-process
messaging, not a hot path.

invariant: `dump` and `validateJson` agree on every key, at every depth
*)
let dumpValue (backend: IJsonBackend) (registry: CodecRegistry) (typ: System.Type) (record: obj) : obj =
    (Plan.forType backend registry camelCaseKey camelCaseTag typ).Encode record

let inline dump<'T> (backend: IJsonBackend) (record: 'T) : obj =
    dumpValue backend Fable.TypedJson.Schema.emptyRegistry typeof<'T> (box record)

/// Dump a record to a backend-native JSON map, resolving registered codecs.
let inline dumpWith<'T> (backend: IJsonBackend) (registry: CodecRegistry) (record: 'T) : obj =
    dumpValue backend registry typeof<'T> (box record)

(**
Validate a backend-native JSON map (from `parseRaw`) straight into `'T`.

Shorthand for a default-configured codec: camelCase keys, no aliases, no model
validator. Anything beyond that — a registry, a case rule, `alias`,
`withModel` — needs a real codec via `auto` / `autoWith`, which is also the
form to use when decoding more than once, since these build a plan per call.

invariant: `dump` and `validateJson` agree on every key, at every depth — a dumped map validates back
*)
let decodeJsonAs<'T> (backend: IJsonBackend) (registry: CodecRegistry) (typ: System.Type) (map: obj) : Result<'T, FieldError list> =
    (Plan.forType backend registry camelCaseKey camelCaseTag typ).Decode map
    |> Result.map unbox<'T>

let inline validateJson<'T> (backend: IJsonBackend) (map: obj) : Result<'T, FieldError list> =
    decodeJsonAs<'T> backend Fable.TypedJson.Schema.emptyRegistry typeof<'T> map

/// Validate a backend-native JSON map, resolving registered codecs.
let inline validateJsonWith<'T> (backend: IJsonBackend) (registry: CodecRegistry) (map: obj) : Result<'T, FieldError list> =
    decodeJsonAs<'T> backend registry typeof<'T> map

(**
Validate a `Map<string, string>` into `'T` — LLM tool-call arguments, form
fields, environment variables. Every value arrives as a string and the
primitive nodes coerce from there, which is the whole reason cross-type
coercion exists.

Reads through a lookup rather than a native map, so no intermediate JSON
object has to be built.
*)
let decodeStringMapAs<'T>
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (typ: System.Type)
    (map: Map<string, string>)
    : Result<'T, FieldError list> =
    (Plan.forTypeFromLookup backend registry camelCaseKey camelCaseTag typ) (stringMapAdapter map)
    |> Result.map unbox<'T>

let inline validateMap<'T> (backend: IJsonBackend) (map: Map<string, string>) : Result<'T, FieldError list> =
    decodeStringMapAs<'T> backend Fable.TypedJson.Schema.emptyRegistry typeof<'T> map

/// Validate a `Map<string, string>`, resolving registered codecs.
let inline validateMapWith<'T> (backend: IJsonBackend) (registry: CodecRegistry) (map: Map<string, string>) : Result<'T, FieldError list> =
    decodeStringMapAs<'T> backend registry typeof<'T> map

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
            inner.decodeWith rules map
            |> Result.bind validator

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
