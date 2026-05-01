(**
# Schema — Format-Agnostic Record Validation

Reflection-based schema that validates F# record types against an abstract
field source. Works with any source via adapters (string maps, BEAM maps, etc.).

principle: separate validation from serialization
principle: coercion is explicit and type-driven via PropertyType.FullName
adr: erased JsonValue DU for zero-cost type-safe values on BEAM
adr: Schema<'T> is a function from lookup to Result — composable and testable
TODO: test
*)

module Fable.TypedJson.Schema

open Fable.Core
open FSharp.Reflection
open Fable.TypedJson.Backend

// ============================================================================
// JsonValue — Erased DU for type-safe BEAM values
// ============================================================================

(**
## JsonValue

Zero-cost erased DU over native BEAM types. At runtime, `JString "hello"`
IS `<<"hello">>`, `JInt 42` IS `42`. Pattern matching compiles to Erlang
type guards (`is_binary`, `is_integer`, `is_float`, `is_boolean`, `is_map`).

adr: erased over obj -- compiler-checked exhaustive matching, zero allocation
*)

[<Erase>]
type JsonValue =
    | JString of string
    | JInt of int
    | JFloat of float
    | JBool of bool
    | JNull
    | JArray of obj
    | JMap of obj

// ============================================================================
// Error Types
// ============================================================================

type FieldError = { path: string; message: string }

// ============================================================================
// IJsonCodec — per-type codec for the validators-as-types pattern
// ============================================================================

(**
## IJsonCodec

A user-defined wrapper type can implement validation by declaring a static
`JsonCodec` member of type `IJsonCodec<'Self>`. `Schema.coerce` discovers
these via reflection on the field's `PropertyType` and dispatches through
them when the type isn't a known primitive.

principle: validation lives with the type — Pydantic validators-as-types in F#
adr: discovered via reflection on a static `JsonCodec` member, not via
     SRTP — F# SRTP can't traverse record fields' heterogeneous types
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
adr: closure-captured boxing avoids reflection on the codec object at decode
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

let registerEntry (fullName: string) (entry: CodecEntry) (registry: CodecRegistry) : CodecRegistry =
    Map.add fullName entry registry

/// Add a codec for type `'T` to a registry, returning a new registry with the entry.
/// Pipeline-friendly: `emptyRegistry |> register Days.JsonCodec |> register Email.JsonCodec`.
let inline register<'T> (codec: IJsonCodec<'T>) (registry: CodecRegistry) : CodecRegistry =
    let entry = {
        decode =
            fun jv ->
                match codec.Decode jv with
                | Ok v -> Ok(box v)
                | Error e -> Error e
        encode = fun v -> codec.Encode(unbox<'T> v)
        schema = codec.Schema
    }

    registerEntry typeof<'T>.FullName entry registry

let tryGetCodecEntry (fullName: string) (registry: CodecRegistry) : CodecEntry option =
    Map.tryFind fullName registry

// ============================================================================
// Schema Type
// ============================================================================

type Schema<'T> = (string -> JsonValue option) -> Result<'T, FieldError list>

// ============================================================================
// Coercion
// ============================================================================

(**
## Coercion

Converts a JsonValue to the target F# type based on PropertyType.FullName.
Handles cross-type coercion (e.g., JString "42" → int 42) which is critical
for ToolCall inputs where the LLM always emits strings.

adr: dispatch by FullName string -- preserved on BEAM by Fable codegen
*)

// ============================================================================
// Reflection Helpers
// ============================================================================

let isOptionType (fullName: string) : bool =
    fullName.StartsWith("Microsoft.FSharp.Core.FSharpOption")

let isFSharpListType (fullName: string) : bool =
    fullName.StartsWith("Microsoft.FSharp.Collections.FSharpList")

let getOptionInnerFullName (fi: System.Reflection.PropertyInfo) : string =
    fi.PropertyType.GenericTypeArguments.[0].FullName

let getGenericInnerType (t: System.Type) : System.Type = t.GenericTypeArguments.[0]

/// Format a list of FieldErrors into a single string (used to collapse nested
/// record / list errors into a single coerce error message).
let private joinErrors (errors: FieldError list) : string =
    errors
    |> List.map (fun e -> sprintf "%s: %s" e.path e.message)
    |> String.concat ", "

let rec coerce
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (targetType: System.Type)
    (fv: JsonValue)
    : Result<obj, string> =
    let targetFullName = targetType.FullName

    match targetFullName, fv with
    // String target — anything can become a string
    | "System.String", JString s -> Ok(box s)
    | "System.String", JInt n -> Ok(box (string n))
    | "System.String", JFloat f -> Ok(box (string f))
    | "System.String", JBool b -> Ok(box (if b then "true" else "false"))

    // Int target
    | "System.Int32", JInt n -> Ok(box n)
    | "System.Int32", JFloat f -> Ok(box (int f))
    | "System.Int32", JString s ->
        match System.Int32.TryParse(s) with
        | true, n -> Ok(box n)
        | _ -> Error(sprintf "cannot parse '%s' as int" s)

    // Int64 target
    | "System.Int64", JInt n -> Ok(box (int64 n))
    | "System.Int64", JFloat f -> Ok(box (int64 f))
    | "System.Int64", JString s ->
        match System.Int64.TryParse(s) with
        | true, n -> Ok(box n)
        | _ -> Error(sprintf "cannot parse '%s' as int64" s)

    // Float target
    | "System.Double", JFloat f -> Ok(box f)
    | "System.Double", JInt n -> Ok(box (float n))
    | "System.Double", JString s ->
        match System.Double.TryParse(s) with
        | true, f -> Ok(box f)
        | _ -> Error(sprintf "cannot parse '%s' as float" s)

    // Bool target
    | "System.Boolean", JBool b -> Ok(box b)
    | "System.Boolean", JString s ->
        match s.ToLower() with
        | "true" -> Ok(box true)
        | "false" -> Ok(box false)
        | _ -> Error(sprintf "cannot parse '%s' as bool" s)

    | _, _ ->
        // 1. User-registered codec wins.
        match tryGetCodecEntry targetFullName registry with
        | Some entry -> entry.decode fv
        | None ->
            // 2. Nested record: recurse via resolveRecord.
            if FSharpType.IsRecord targetType then
                if backend.IsMap(box fv) then
                    let inner = unbox<obj> fv

                    let lookup (key: string) : JsonValue option =
                        if backend.ContainsKey(inner, key) then
                            Some(unbox<JsonValue> (backend.Get(inner, key)))
                        else
                            None

                    match resolveRecord backend registry targetType lookup with
                    | Ok r -> Ok r
                    | Error errs -> Error(joinErrors errs)
                else
                    Error(sprintf "expected JSON object for %s" targetType.Name)

            // 3. F# list (`'T list`).
            elif isFSharpListType targetFullName then
                let elementType = getGenericInnerType targetType
                coerceList backend registry elementType fv

            // 4. .NET array (`'T[]`).
            elif targetType.IsArray then
                let elementType = targetType.GetElementType()
                coerceArray backend registry elementType fv

            else
                // Genuine type mismatch.
                let valueDesc =
                    match fv with
                    | JString s -> sprintf "string '%s'" s
                    | JInt n -> sprintf "int %d" n
                    | JFloat f -> sprintf "float %f" f
                    | JBool b -> sprintf "bool %b" b
                    | JNull -> "null"
                    | JArray _ -> "array"
                    | JMap _ -> "map"

                Error(sprintf "cannot coerce %s to %s" valueDesc targetFullName)

and private coerceElements
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (elementType: System.Type)
    (fv: JsonValue)
    : Result<obj list, string> =
    // The native sequence shape varies per backend (Erlang list, Python list,
    // JS array, .NET array). We delegate length + index access to the backend
    // rather than reaching for F# `obj list` or `obj[]` — the former needs
    // FSharpList cons cells (broken on Python's native lists), the latter
    // becomes a process-dictionary ref on BEAM. Backend-mediated indexing
    // handles all targets uniformly.
    if not (backend.IsArray(box fv)) then
        Error(sprintf "expected JSON array for %s[]" elementType.Name)
    else
        let arr = box fv
        let len = backend.ArrayLength arr
        let mutable i = 0
        let mutable err: string option = None
        let mutable acc: obj list = []

        while i < len && err.IsNone do
            let head = backend.ArrayAt(arr, i)

            match coerce backend registry elementType (unbox<JsonValue> head) with
            | Ok v -> acc <- v :: acc
            | Error msg -> err <- Some(sprintf "[%d] %s" i msg)

            i <- i + 1

        match err with
        | Some msg -> Error msg
        | None -> Ok(List.rev acc)

and coerceList
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (elementType: System.Type)
    (fv: JsonValue)
    : Result<obj, string> =
    // Fable erases generics at runtime, so `obj list` and `'T list` share
    // representation. Box and let the consumer pattern-match.
    match coerceElements backend registry elementType fv with
    | Ok xs -> Ok(box xs)
    | Error msg -> Error msg

and coerceArray
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (elementType: System.Type)
    (fv: JsonValue)
    : Result<obj, string> =
    match coerceElements backend registry elementType fv with
    | Ok xs -> Ok(box (List.toArray xs))
    | Error msg -> Error msg

and resolveRecord
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (recordType: System.Type)
    (lookup: string -> JsonValue option)
    : Result<obj, FieldError list> =
    let fields = FSharpType.GetRecordFields recordType

    let results =
        fields |> Array.map (fun fi -> resolveField backend registry fi lookup)

    let errors =
        results
        |> Array.choose (function
            | Error e -> Some e
            | Ok _ -> None)
        |> Array.toList

    if errors.IsEmpty then
        let values =
            results
            |> Array.map (fun r ->
                match r with
                | Ok v -> v
                | Error _ -> box null)

        Ok(FSharpValue.MakeRecord(recordType, values))
    else
        Error errors

and resolveOptionalField
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (fi: System.Reflection.PropertyInfo)
    (name: string)
    (value: JsonValue option)
    : Result<obj, FieldError> =
    match value with
    | None
    | Some JNull -> Ok(box None)
    | Some fv ->
        let innerType = getGenericInnerType fi.PropertyType

        match coerce backend registry innerType fv with
        | Ok v -> Ok(box (Some v))
        | Error msg -> Error { path = name; message = msg }

and resolveRequiredField
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (fi: System.Reflection.PropertyInfo)
    (name: string)
    (value: JsonValue option)
    : Result<obj, FieldError> =
    match value with
    | None -> Error { path = name; message = sprintf "missing field (expected %s)" fi.PropertyType.Name }
    | Some JNull -> Error { path = name; message = sprintf "null value (expected %s)" fi.PropertyType.Name }
    | Some fv ->
        match coerce backend registry fi.PropertyType fv with
        | Ok v -> Ok v
        | Error msg -> Error { path = name; message = msg }

and resolveField
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (fi: System.Reflection.PropertyInfo)
    (lookup: string -> JsonValue option)
    : Result<obj, FieldError> =
    let name = fi.Name
    let value = lookup name

    if isOptionType fi.PropertyType.FullName then
        resolveOptionalField backend registry fi name value
    else
        resolveRequiredField backend registry fi name value

// ============================================================================
// Schema.auto
// ============================================================================

(**
## auto

Creates a Schema for an F# record type using FSharp.Reflection.
Field names from reflection (snake_case on BEAM) are used as lookup keys.

invariant: all fields validated, errors accumulated (not fail-fast)
adr: inline required so Fable resolves typeof<'T> at each call site
*)

let inline auto<'T> (backend: IJsonBackend) (registry: CodecRegistry) : Schema<'T> =
    let typ = typeof<'T>

    fun (lookup: string -> JsonValue option) ->
        match resolveRecord backend registry typ lookup with
        | Ok r -> Ok(unbox<'T> r)
        | Error errs -> Error errs

// ============================================================================
// Adapters
// ============================================================================

(**
## Adapters

Convert various source formats to the `string -> JsonValue option` lookup.
*)

/// Adapt a Map<string, string> (e.g., ToolCall.input from LLM).
let stringMapAdapter (map: Map<string, string>) (key: string) : JsonValue option =
    match Map.tryFind key map with
    | Some v -> Some(JString v)
    | None -> None

/// Adapt a backend-native JSON map (e.g., parsed via `IJsonBackend.Parse`).
/// Values are unboxed to JsonValue (zero-cost, erased).
let jsonMapAdapter (backend: IJsonBackend) (map: obj) (key: string) : JsonValue option =
    if backend.ContainsKey(map, key) then
        Some(unbox<JsonValue> (backend.Get(map, key)))
    else
        None

// ============================================================================
// Convenience Functions
// ============================================================================

/// Validate from a string map (ToolCall.input from LLM). Uses an empty codec registry.
let inline validateMap<'T> (backend: IJsonBackend) (map: Map<string, string>) : Result<'T, FieldError list> =
    (auto<'T> backend emptyRegistry) (stringMapAdapter map)

/// Validate from a string map with a custom codec registry.
let inline validateMapWith<'T>
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (map: Map<string, string>)
    : Result<'T, FieldError list> =
    (auto<'T> backend registry) (stringMapAdapter map)

/// Validate from a backend-native JSON map. Uses an empty codec registry.
let inline validateJson<'T> (backend: IJsonBackend) (map: obj) : Result<'T, FieldError list> =
    (auto<'T> backend emptyRegistry) (jsonMapAdapter backend map)

/// Validate from a backend-native JSON map with a custom codec registry.
let inline validateJsonWith<'T>
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (map: obj)
    : Result<'T, FieldError list> =
    (auto<'T> backend registry) (jsonMapAdapter backend map)

/// Dump a record to a backend-native JSON map (e.g., for inter-process messaging).
let inline dump<'T> (backend: IJsonBackend) (record: 'T) : obj =
    let typ = typeof<'T>
    let fields = FSharpType.GetRecordFields typ
    let values = FSharpValue.GetRecordFields(box record)

    Array.zip fields values
    |> Array.fold
        (fun acc (fi, v) ->
            if isOptionType fi.PropertyType.FullName then
                match unbox<obj option> v with
                | Some inner -> backend.Put(acc, fi.Name, inner)
                | None -> acc
            else
                backend.Put(acc, fi.Name, v))
        (backend.NewMap())

/// Format errors into a human-readable string for LLM feedback.
let formatErrors (errors: FieldError list) : string =
    errors
    |> List.map (fun e -> sprintf "%s: %s" e.path e.message)
    |> String.concat ", "
