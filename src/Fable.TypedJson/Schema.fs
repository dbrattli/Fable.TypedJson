(**
# Schema — shared vocabulary and reflection primitives

What every layer above needs and none of them should define twice: the
`JsonValue` DU that user codecs speak, `FieldError`, the JSON Schema tree, the
`IJsonCodec` interface and its registry, and the per-target reflection helpers
that paper over CLR generic invariance.

`Plan` owns the single type traversal for decoding, encoding, and schema
generation; this module provides the shared types and target-portable helpers
that traversal consumes.

decision: delegates runtime reflection variance to Optimizations — callers share one planning algorithm
*)

module Fable.TypedJson.Schema

open Fable.Core
open FSharp.Reflection
open Fable.TypedJson.Backend

// ============================================================================
// JsonValue — User-codec-facing JSON value DU
// ============================================================================

(**
## JsonValue

The DU exchanged by `IJsonCodec.Decode` and `Encode`. Internal plan nodes use
backend-native values through `IJsonBackend.IsX` / `AsX`; `toJsonValue` wraps a
value only when it crosses into a user codec.

decision: uses a tagged struct DU rather than erased cases — `JArray` and `JMap` both carry `obj` and must remain distinguishable
invariant: `JsonValue` is constructed only at the user-codec boundary, never on the built-in primitive hot path
tradeoff: converts values at custom-codec boundaries to keep the public codec representation portable and unambiguous
*)

[<Struct>]
type JsonValue =
    | JString of stringValue: string
    | JInt of intValue: int
    | JFloat of floatValue: float
    | JBool of boolValue: bool
    | JNull
    | JArray of arrayValue: obj
    | JMap of mapValue: obj

// ============================================================================
// Error Types
// ============================================================================

// A `FieldError list` already allocates its cons cells, so a two-reference
// struct inlines the error payload instead of adding one object per failure.
// decision: stores `FieldError` as a struct — validation-heavy failures avoid a separate allocation per field
// tradeoff: copies two references per error value to remove the extra heap object
[<Struct>]
type FieldError = { path: string; message: string }

// ============================================================================
// IJsonCodec — per-type codec for the validators-as-types pattern
// ============================================================================

(**
## IJsonCodec

A user-defined wrapper type can keep an `IJsonCodec<'Self>` in a static
`JsonCodec` member, but the codec must be added explicitly to a
`CodecRegistry`. `Plan` resolves registered codecs from the field's
`PropertyType` when it constructs the plan.

decision: keeps validation with the wrapper type — one codec controls its decode, encode, and schema representation
decision: requires explicit codec registration — portable static-member discovery is not available on all four targets
*)

// ---------------------------------------------------------------------------
// JsonSchema — typed tree representing a JSON Schema fragment.
// Used by IJsonCodec.Schema and the reflection-driven `jsonSchemaOf` walker.
// Map<string, JsonSchemaValue> represents a JSON object; values are tagged
// so we can emit the right runtime types per backend (integer vs. number,
// string vs. nested object, etc.) without losing information.
// ---------------------------------------------------------------------------
type JsonSchemaValue =
    | SVStr of string
    | SVInt of int
    | SVFloat of float
    | SVBool of bool
    | SVList of JsonSchemaValue list
    | SVDict of Map<string, JsonSchemaValue>

type JsonSchema = Map<string, JsonSchemaValue>

let emptySchema: JsonSchema = Map.empty

/// A schema for a JSON value of a given primitive type ("string", "integer",
/// "number", "boolean", "object", "array", "null").
let primitiveSchema (typeName: string) : JsonSchema = Map.ofList [ "type", SVStr typeName ]

type IJsonCodec<'T> =
    abstract member Decode: JsonValue -> Result<'T, string>
    abstract member Encode: 'T -> JsonValue
    /// JSON Schema fragment describing what shape this codec accepts.
    /// Combinators (gt, lt, minLength, pattern, ...) extend this with the
    /// matching JSON Schema constraint keys ("exclusiveMinimum", "minLength", ...).
    abstract member Schema: JsonSchema

// ============================================================================
// Codec Registry — immutable per-type lookup, keyed by FullName
// ============================================================================

(**
## Registry

A typed codec is registered against `typeof<'T>.FullName`, capturing a
boxing closure pair. Keys are strings so the registry is portable to every
Fable backend without relying on runtime type identity beyond
`typeof<'T>.FullName`.

The registry is an **immutable Map** threaded explicitly through `auto`,
not a mutable global. This is required because Fable BEAM compiles
module-level `let mut` / `Dictionary` bindings to fresh-per-call values,
so a global mutable registry doesn't survive across calls. Threading is
also more predictable and Fable-portable.

invariant: register returns a NEW registry; the original is unchanged
decision: captures typed boxing closures at registration — decoding values does not reflect over codec instances
*)

type CodecEntry = {
    decode: JsonValue -> Result<obj, string>
    encode: obj -> JsonValue
    /// JSON Schema fragment for this codec. Stored on registration so the
    /// schema generator can emit the right schema for fields whose type lives
    /// in the registry (refined types, user wrapper DUs, ...) instead of
    /// falling back to an empty object.
    schema: JsonSchema
}

type CodecRegistry = Map<string, CodecEntry>

let emptyRegistry: CodecRegistry = Map.empty

let registerEntry (fullName: string) (entry: CodecEntry) (registry: CodecRegistry) : CodecRegistry = Map.add fullName entry registry

/// Add a codec for type `'T` to a registry, returning a new registry with the entry.
/// Pipeline-friendly: `emptyRegistry |> register Days.JsonCodec |> register Email.JsonCodec`.
let inline register<'T> (codec: IJsonCodec<'T>) (registry: CodecRegistry) : CodecRegistry =
    let entry = {
        decode = codec.Decode >> Result.map box
        encode = unbox<'T> >> codec.Encode
        schema = codec.Schema
    }

    registerEntry typeof<'T>.FullName entry registry

let tryGetCodecEntry (fullName: string) (registry: CodecRegistry) : CodecEntry option = Map.tryFind fullName registry

// ============================================================================
// Schema Type
// ============================================================================

/// A decoder over a keyed source of backend-native values.
///
/// decision: retains this public alias after the planner stopped using it — removing it would be source-breaking
type Schema<'T> = (string -> obj option) -> Result<'T, FieldError list>

// ============================================================================
// Type classification
// ============================================================================

(**
## Type classification

The planner resolves portable type categories once at codec construction.

decision: classifies types by `FullName` strings — Fable preserves names where runtime type identity is not portable
*)

// ============================================================================
// Reflection Helpers
// ============================================================================

/// True for the primitive types the planner resolves directly, ahead of any
/// registered codec. Encode-side dispatch consults this for the same reason,
/// so a codec registered against e.g. `System.Int32` is inert in BOTH
/// directions rather than taking effect on encode only.
///
/// invariant: primitive dispatch precedes registry dispatch on both the decode and encode paths
let isPrimitiveType (fullName: string) : bool =
    fullName = "System.String"
    || fullName = "System.Int32"
    || fullName = "System.Int64"
    || fullName = "System.Double"
    || fullName = "System.Boolean"

let isOptionType (fullName: string) : bool =
    fullName.StartsWith("Microsoft.FSharp.Core.FSharpOption")

let isFSharpListType (fullName: string) : bool =
    fullName.StartsWith("Microsoft.FSharp.Collections.FSharpList")

/// Runtime representation helpers. Implementations live in Optimizations;
/// these forwarding functions preserve the existing Schema API.
let inline extractOption (v: obj) : obj option =
    Optimizations.Reflection.extractOption v

let inline extractList (v: obj) : obj list = Optimizations.Reflection.extractList v

let inline extractArray (v: obj) : obj list = Optimizations.Reflection.extractArray v

let inline optionBuilder (innerType: System.Type) : obj -> obj =
    Optimizations.Reflection.optionBuilder innerType

let inline listBuilder (elementType: System.Type) : obj list -> obj =
    Optimizations.Reflection.listBuilder elementType

let inline arrayBuilder (elementType: System.Type) : obj list -> obj =
    Optimizations.Reflection.arrayBuilder elementType

let inline recordCtor (t: System.Type) : obj[] -> obj = Optimizations.Reflection.recordCtor t

let inline fieldReader (fi: System.Reflection.PropertyInfo) : obj -> obj = Optimizations.Reflection.fieldReader fi

let getGenericInnerType (t: System.Type) : System.Type = t.GenericTypeArguments.[0]

/// Format a list of FieldErrors into a single human-readable string.
/// Collapses nested record / list errors into a single message and is the
/// public LLM-feedback formatter — one definition serving both call sites.
let formatErrors (errors: FieldError list) : string =
    errors
    |> List.map (fun e -> sprintf "%s: %s" e.path e.message)
    |> String.concat ", "

/// Identity key transform — passes the F# field name through unchanged.
/// Useful when callers control both the lookup keys and the F# field names.
let identityTransform (s: string) : string = s

(**
Public `LowerFirst` (camelCase) compatibility transform. The shortcut APIs now
build plans through `Json.resolveKey`, which uses the same `Casing` functions.

It normalizes through `Casing.toCanonicalPascal` first, so a backend that hands
back a snake_case name yields the same key as one that hands back the F# name:
`air_temperature` and `AirTemperature` both produce `airTemperature`. Without
that pivot the key tracked whichever spelling the compiler produced — BEAM
emitted `air_temperature` before Fable 5.8.1 and Python before 5.14.0, while JS
and .NET emitted `airTemperature`.

invariant: this helper and `Json.applyCaseRule LowerFirst` derive the same key on every target
*)
let lowerFirstTransform (s: string) : string =
    Casing.lowerFirst (Casing.toCanonicalPascal s)

/// Wrap a backend-native value as a `JsonValue` for hand-off to a user codec.
/// Built-in plan nodes stay on `IJsonBackend.IsX` / `AsX` and do not call this.
let toJsonValue (backend: IJsonBackend) (fv: obj) : JsonValue =
    if backend.IsString fv then
        JString(backend.AsString fv)
    elif backend.IsInt fv then
        JInt(backend.AsInt fv)
    elif backend.IsFloat fv then
        JFloat(backend.AsFloat fv)
    elif backend.IsBool fv then
        JBool(backend.AsBool fv)
    elif backend.IsNull fv then
        JNull
    elif backend.IsArray fv then
        JArray fv
    elif backend.IsMap fv then
        JMap fv
    else
        failwithf "toJsonValue: unrecognised value of type %s" (fv.GetType().FullName)

/// Unwrap a `JsonValue` handed back by a user codec's `Encode` into the
/// backend-native form the encode path builds maps out of. The inverse of
/// `toJsonValue`, and the encode-side counterpart to `CodecEntry.decode`.
///
/// `JArray` / `JMap` payloads are already backend-native (that is what
/// `toJsonValue` put in them), so they pass straight through.
let fromJsonValue (backend: IJsonBackend) (jv: JsonValue) : obj =
    match jv with
    | JString s -> box s
    | JInt n -> box n
    | JFloat f -> box f
    | JBool b -> box b
    | JNull -> backend.Null
    | JArray a -> a
    | JMap m -> m

/// Render a backend-native value as a short human-readable string for
/// error messages without first constructing a `JsonValue`.
let describeValue (backend: IJsonBackend) (fv: obj) : string =
    if backend.IsString fv then
        sprintf "string '%s'" (backend.AsString fv)
    elif backend.IsInt fv then
        sprintf "int %d" (backend.AsInt fv)
    elif backend.IsFloat fv then
        sprintf "float %f" (backend.AsFloat fv)
    elif backend.IsBool fv then
        sprintf "bool %b" (backend.AsBool fv)
    elif backend.IsNull fv then
        "null"
    elif backend.IsArray fv then
        "array"
    elif backend.IsMap fv then
        "map"
    else
        "<unknown>"

/// Build a `key -> obj option` lookup over a backend-native JSON map.
/// One implementation backing both the internal record/union resolvers and
/// the public adapters — declared ahead of the walker
/// so the recursive group can reference it.
let mapLookup (backend: IJsonBackend) (m: obj) (key: string) : obj option = backend.TryGet(m, key)

// ============================================================================
// Adapters
// ============================================================================

(**
## Adapters

Convert various source formats to the `string -> obj option` lookup.
*)

/// Adapt a Map<string, string> (e.g., ToolCall.input from LLM). Each value
/// is the raw F# string; primitive plan nodes classify it through
/// `backend.IsString` and can coerce it to int, float, or bool.
let stringMapAdapter (map: Map<string, string>) (key: string) : obj option =
    match Map.tryFind key map with
    | Some v -> Some(box v)
    | None -> None

(**
The two faces a keyed non-JSON source presents to `Plan.forTypeFromLookup`.

`Get` is the per-key read the record and union walks do — one lookup per
field. `AsMap` materialises the whole source as a backend-native map, which
only a codec registered against the *top-level* type ever asks for: such a
codec owns the shape and takes its value whole, exactly as it does on the JSON
path, where `toJsonValue` hands it the parsed object as a `JMap`.

`AsMap` is a thunk, not a value, because the structural walks — the common
case by far — must not pay to build a map they never read.

invariant: a registered codec sees the same `JMap` shape whether its value arrived as parsed JSON or as a string map
*)
type LookupSource = {
    Get: string -> obj option
    AsMap: unit -> obj
}

/// `stringMapAdapter` plus the whole-map face. Values stay raw F# strings —
/// the same shape `Get` hands out, and what every backend's `IsString` /
/// `AsString` pair already accepts.
let stringMapSource (backend: IJsonBackend) (map: Map<string, string>) : LookupSource = {
    Get = stringMapAdapter map
    AsMap =
        fun () ->
            map
            |> Map.fold (fun acc key value -> backend.Put(acc, key, box value)) (backend.NewMap())
}
