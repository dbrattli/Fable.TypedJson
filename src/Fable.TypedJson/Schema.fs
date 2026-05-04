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
// JsonValue — User-codec-facing JSON value DU
// ============================================================================

(**
## JsonValue

The DU `IJsonCodec.Decode` / `Encode` exchange with user codecs. The
internal `coerce` / schema layer never constructs or pattern-matches
this — they go through `IJsonBackend.IsX` / `AsX` directly, so on the
hot path no `JsonValue` ever exists. `toJsonValue` lazily wraps a
backend-native value into the DU only when crossing into a user codec.

Was `[<Erase>]` historically — that gave Fable backends a zero-cost
view over native values. After moving the schema dispatch to the
backend's typed accessors, the DU is no longer on the hot path, so the
erasure win doesn't matter and the erased form has design constraints
(two `of obj` cases would now be rejected by Fable's stricter check
since the case payloads can't be runtime-distinguished). `[<Struct>]`
keeps the DU as a tagged value type on the CLR (no heap allocation per
case construction); Fable ignores `[<Struct>]` and lowers the DU to
its usual per-target representation, which is fine because user-codec
calls are infrequent enough that the per-construction allocation isn't
a hot-path concern.
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

// Struct because FieldError is allocated per failing field on the error
// path. For success-path workloads (the common case) this is dead weight,
// but for validation-heavy LLM-tool-input workloads it cuts a heap alloc
// per error. The struct is two references (16 bytes on x64), small enough
// that copy cost is below alloc cost — and the typical use site stores it
// in a `FieldError list` cons cell which already heap-allocates, so the
// struct just inlines into the cons cell with no extra cost.
[<Struct>]
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

let registerEntry (fullName: string) (entry: CodecEntry) (registry: CodecRegistry) : CodecRegistry = Map.add fullName entry registry

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

let tryGetCodecEntry (fullName: string) (registry: CodecRegistry) : CodecEntry option = Map.tryFind fullName registry

// ============================================================================
// Schema Type
// ============================================================================

// `Schema<'T>` lookup hands back the backend-native value (`obj`) rather
// than a `JsonValue`. Internal coerce uses `IJsonBackend.IsX` / `AsX` to
// dispatch directly on the native shape; `JsonValue` is only constructed
// (lazily, via `toJsonValue`) when crossing into user codecs that take it.
type Schema<'T> = (string -> obj option) -> Result<'T, FieldError list>

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

(**
## Variance helpers — `extractOption` / `extractList`

Fable backends compile F# generics with type erasure, so `unbox<obj option>
(box (Some "x"))` round-trips by runtime identity. The CLR keeps `'T option`
generic-invariant, so the same `unbox` raises `InvalidCastException` on the
.NET shim. These helpers go through `FSharpValue.GetUnionFields`, which is
provided uniformly by both Fable's reflection and the CLR.

assumption: `Option<'T>` Tag 1 = `Some`, Tag 0 = `None`; `List<'T>` Tag 1
            = `Cons`, Tag 0 = `Empty`. Both are stable parts of the F#
            runtime contract.
*)

#if FABLE_COMPILER
let inline extractOption (v: obj) : obj option = unbox<obj option> v

let inline extractList (v: obj) : obj list = unbox<obj list> v

/// Build an `'innerType option` from an inner value. On Fable this is just
/// `box (Some v)` because erasure makes all option cells share a runtime
/// representation; on the CLR we need to construct the typed union case.
let inline buildOption (_innerType: System.Type) (v: obj) : obj = box (Some v)

/// Build a `'elementType list` from an `obj list`. Same erasure rationale
/// as `buildOption`.
let inline buildList (_elementType: System.Type) (xs: obj list) : obj = box xs
#else
let extractOption (v: obj) : obj option =
    if isNull v then
        None
    else
        let case, fields = FSharpValue.GetUnionFields(v, v.GetType())

        if case.Tag = 1 then Some fields.[0] else None

let extractList (v: obj) : obj list =
    let typ = v.GetType()
    let mutable current = v
    let mutable acc: obj list = []
    let mutable finished = false

    while not finished do
        let case, fields = FSharpValue.GetUnionFields(current, typ)
        // FSharpList: Empty has Tag 0 (Nil), Cons has Tag 1 (head :: tail).
        if case.Tag = 1 then
            acc <- fields.[0] :: acc
            current <- fields.[1]
        else
            finished <- true

    List.rev acc

/// Build an `'innerType option` by reflecting the option's `Some` case for
/// the target generic. `Option<'T>` is invariant on the CLR, so a plain
/// `box (Some v)` produces `Option<obj>` and assigning it to a typed
/// record field via reflection raises `ArgumentException`. Fable erases
/// the generic so its `box (Some v)` is enough.
let buildOption (innerType: System.Type) (v: obj) : obj =
    let optType = typedefof<_ option>.MakeGenericType([| innerType |])
    let cases = FSharpType.GetUnionCases(optType)
    let someCase = cases |> Array.find (fun c -> c.Tag = 1)
    FSharpValue.MakeUnion(someCase, [| v |])

/// Build a `'elementType list` by walking `xs` and constructing a chain of
/// typed `Cons` cells terminating in the typed `Empty`. Same invariance
/// rationale as `buildOption`.
let buildList (elementType: System.Type) (xs: obj list) : obj =
    let listType = typedefof<_ list>.MakeGenericType([| elementType |])
    let cases = FSharpType.GetUnionCases(listType)
    let emptyCase = cases |> Array.find (fun c -> c.Tag = 0)
    let consCase = cases |> Array.find (fun c -> c.Tag = 1)
    let mutable acc = FSharpValue.MakeUnion(emptyCase, [||])

    for x in List.rev xs do
        acc <- FSharpValue.MakeUnion(consCase, [| x; acc |])

    acc
#endif

let getOptionInnerFullName (fi: System.Reflection.PropertyInfo) : string =
    fi.PropertyType.GenericTypeArguments.[0].FullName

let getGenericInnerType (t: System.Type) : System.Type = t.GenericTypeArguments.[0]

/// Format a list of FieldErrors into a single human-readable string.
/// Collapses nested record / list errors into a single message and is the
/// public LLM-feedback formatter — one definition serving both call sites.
let formatErrors (errors: FieldError list) : string =
    errors
    |> List.map (fun e -> sprintf "%s: %s" e.path e.message)
    |> String.concat ", "

/// Identity key transform — passes the F# field name through unchanged.
/// Useful when callers control both the lookup keys and the F# field names
/// (rare). Most call sites should use `lowerFirstTransform` so the JSON key
/// is consistent across backends (BEAM and Python lowercase F# field
/// names in reflection; JS preserves PascalCase).
let identityTransform (s: string) : string = s

/// Default key transform — `LowerFirst` (camelCase). Lowercases the first
/// character; if the input is already snake_case (no uppercase letters)
/// returns it unchanged. Stable across BEAM/Python (already lowercase) and
/// JS (PascalCase → camelCase).
let lowerFirstTransform (s: string) : string =
    if s.Length = 0 || not (String.exists System.Char.IsUpper s) then
        s
    else
        string (System.Char.ToLowerInvariant s.[0]) + s.[1..]

/// Lazily wrap a backend-native value as a `JsonValue` for hand-off to
/// user codecs (`IJsonCodec.Decode`). Internal `coerce` never calls this
/// — it routes through `IJsonBackend.IsX` / `AsX` directly. On Fable
/// backends each `JString s` etc. is identity (no allocation thanks to
/// `[<Erase>]` legacy / Fable's representation of struct DUs); on the
/// .NET shim each call allocates a small DU instance.
let toJsonValue (backend: IJsonBackend) (fv: obj) : JsonValue =
    if backend.IsString fv then JString(backend.AsString fv)
    elif backend.IsInt fv then JInt(backend.AsInt fv)
    elif backend.IsFloat fv then JFloat(backend.AsFloat fv)
    elif backend.IsBool fv then JBool(backend.AsBool fv)
    elif backend.IsNull fv then JNull
    elif backend.IsArray fv then JArray fv
    elif backend.IsMap fv then JMap fv
    else
        failwithf "toJsonValue: unrecognised value of type %s" (fv.GetType().FullName)

/// Render a backend-native value as a short human-readable string for
/// error messages — replaces the JsonValue pattern match the old
/// coerce-error path used.
let private describeValue (backend: IJsonBackend) (fv: obj) : string =
    if backend.IsString fv then sprintf "string '%s'" (backend.AsString fv)
    elif backend.IsInt fv then sprintf "int %d" (backend.AsInt fv)
    elif backend.IsFloat fv then sprintf "float %f" (backend.AsFloat fv)
    elif backend.IsBool fv then sprintf "bool %b" (backend.AsBool fv)
    elif backend.IsNull fv then "null"
    elif backend.IsArray fv then "array"
    elif backend.IsMap fv then "map"
    else "<unknown>"

/// `coerce` walks the type tree to convert a backend-native value into
/// the target F# type. Dispatches on `targetFullName` for primitives and
/// on F# reflection (`FSharpType.IsRecord` / `IsUnion`, list/array
/// detection) for compound types. The `keyTransform` parameter
/// encapsulates the codec's case rule + aliases so nested-record /
/// nested-union sub-lookups apply the same transformation the outer
/// record did.
let rec coerce
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (targetType: System.Type)
    (fv: obj)
    : Result<obj, string> =
    let targetFullName = targetType.FullName

    // String target — anything primitive can become a string.
    if targetFullName = "System.String" then
        if backend.IsString fv then Ok(box (backend.AsString fv))
        elif backend.IsInt fv then Ok(box (string (backend.AsInt fv)))
        elif backend.IsFloat fv then Ok(box (string (backend.AsFloat fv)))
        elif backend.IsBool fv then
            Ok(box (if backend.AsBool fv then "true" else "false"))
        else
            Error(sprintf "cannot coerce %s to %s" (describeValue backend fv) targetFullName)

    // Int target
    elif targetFullName = "System.Int32" then
        if backend.IsInt fv then Ok(box (backend.AsInt fv))
        elif backend.IsFloat fv then Ok(box (int (backend.AsFloat fv)))
        elif backend.IsString fv then
            let s = backend.AsString fv

            match System.Int32.TryParse(s) with
            | true, n -> Ok(box n)
            | _ -> Error(sprintf "cannot parse '%s' as int" s)
        else
            Error(sprintf "cannot coerce %s to %s" (describeValue backend fv) targetFullName)

    // Int64 target
    elif targetFullName = "System.Int64" then
        if backend.IsInt fv then Ok(box (int64 (backend.AsInt fv)))
        elif backend.IsFloat fv then Ok(box (int64 (backend.AsFloat fv)))
        elif backend.IsString fv then
            let s = backend.AsString fv

            match System.Int64.TryParse(s) with
            | true, n -> Ok(box n)
            | _ -> Error(sprintf "cannot parse '%s' as int64" s)
        else
            Error(sprintf "cannot coerce %s to %s" (describeValue backend fv) targetFullName)

    // Float target
    elif targetFullName = "System.Double" then
        if backend.IsFloat fv then Ok(box (backend.AsFloat fv))
        elif backend.IsInt fv then Ok(box (float (backend.AsInt fv)))
        elif backend.IsString fv then
            let s = backend.AsString fv

            match System.Double.TryParse(s) with
            | true, f -> Ok(box f)
            | _ -> Error(sprintf "cannot parse '%s' as float" s)
        else
            Error(sprintf "cannot coerce %s to %s" (describeValue backend fv) targetFullName)

    // Bool target
    elif targetFullName = "System.Boolean" then
        if backend.IsBool fv then Ok(box (backend.AsBool fv))
        elif backend.IsString fv then
            let s = backend.AsString fv

            match s.ToLower() with
            | "true" -> Ok(box true)
            | "false" -> Ok(box false)
            | _ -> Error(sprintf "cannot parse '%s' as bool" s)
        else
            Error(sprintf "cannot coerce %s to %s" (describeValue backend fv) targetFullName)

    else
        // Non-primitive dispatch: registry codec, nested record, list, union, array.
        match tryGetCodecEntry targetFullName registry with
        // 1. User-registered codec wins. Construct a JsonValue for it now.
        | Some entry -> entry.decode (toJsonValue backend fv)
        | None ->
            // 2. Nested record: recurse via resolveRecord.
            if FSharpType.IsRecord targetType then
                if backend.IsMap fv then
                    // resolveField applies keyTransform before calling the
                    // lookup, so the key here is already in JSON form.
                    let lookup (key: string) : obj option =
                        if backend.ContainsKey(fv, key) then
                            Some(backend.Get(fv, key))
                        else
                            None

                    resolveRecord backend registry keyTransform targetType lookup
                    |> Result.mapError formatErrors
                else
                    Error(sprintf "expected JSON object for %s" targetType.Name)

            // 3. F# list (`'T list`). MUST come before the `IsUnion` check —
            //    `FSharpList<'T>` is itself a DU (Cons | Nil), so on the
            //    CLR, `IsUnion` returns true for it and would mis-route to
            //    the tagged-union path. Fable backends happen to skip this
            //    misclassification because their reflection treats lists
            //    as a primitive sequence type.
            elif isFSharpListType targetFullName then
                let elementType = getGenericInnerType targetType
                coerceList backend registry keyTransform elementType fv

            // 4. Tagged discriminated union.
            elif FSharpType.IsUnion targetType then
                if backend.IsMap fv then
                    coerceUnion backend registry keyTransform targetType fv
                else
                    Error(sprintf "expected JSON object for %s" targetType.Name)

            // 5. .NET array (`'T[]`).
            elif targetType.IsArray then
                let elementType = targetType.GetElementType()
                coerceArray backend registry keyTransform elementType fv

            else
                Error(sprintf "cannot coerce %s to %s" (describeValue backend fv) targetFullName)

and private coerceElements
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (elementType: System.Type)
    (fv: obj)
    : Result<obj list, string> =
    // The native sequence shape varies per backend (Erlang list, Python list,
    // JS array, .NET array). We delegate length + index access to the backend
    // rather than reaching for F# `obj list` or `obj[]` — the former needs
    // FSharpList cons cells (broken on Python's native lists), the latter
    // becomes a process-dictionary ref on BEAM. Backend-mediated indexing
    // handles all targets uniformly.
    if not (backend.IsArray fv) then
        Error(sprintf "expected JSON array for %s[]" elementType.Name)
    else
        let len = backend.ArrayLength fv
        let mutable i = 0
        let mutable err: string option = None
        let mutable acc: obj list = []

        while i < len && err.IsNone do
            let head = backend.ArrayAt(fv, i)

            match coerce backend registry keyTransform elementType head with
            | Ok v -> acc <- v :: acc
            | Error msg -> err <- Some(sprintf "[%d] %s" i msg)

            i <- i + 1

        match err with
        | Some msg -> Error msg
        | None -> Ok(List.rev acc)

and coerceList
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (elementType: System.Type)
    (fv: obj)
    : Result<obj, string> =
    // Build a typed `'elementType list` so the boxed result can be assigned
    // to a record field via reflection. Fable erases generics so the helper
    // is just `box xs`; on the CLR it constructs the typed union cells.
    coerceElements backend registry keyTransform elementType fv
    |> Result.map (buildList elementType)

and coerceArray
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (elementType: System.Type)
    (fv: obj)
    : Result<obj, string> =
    coerceElements backend registry keyTransform elementType fv
    |> Result.map (List.toArray >> box)

and resolveRecord
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (recordType: System.Type)
    (lookup: string -> obj option)
    : Result<obj, FieldError list> =
    let fields = FSharpType.GetRecordFields recordType

    let results =
        fields
        |> Array.map (fun fi -> resolveField backend registry keyTransform fi lookup)

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

/// Default discriminator key for tagged DUs. Matches Pydantic / OpenAPI
/// convention. v1 hardcodes this; a future combinator can override per codec.
and discriminatorKey = "type"

/// Transform an F# union case name into the JSON tag value the dispatcher
/// looks up. Applies LowerFirst — `Search` → `"search"`, `CalculateExpression`
/// → `"calculateExpression"`. This matches what most JSON APIs and Pydantic
/// emit by default. Users wanting snake_case / PascalCase tags can register
/// a custom codec for the union type.
and tagOfCaseName (caseName: string) : string =
    if caseName.Length = 0 then
        caseName
    else
        string (System.Char.ToLowerInvariant caseName.[0])
        + caseName.[1..]

/// Resolve a tagged DU from a key→JsonValue lookup. Used both by the
/// top-level `Schema.auto` (which only has a lookup) and indirectly via
/// `coerceUnion` (which builds a lookup from a backend-native map).
///
/// 1. Read the discriminator key (default `"type"`).
/// 2. Find the matching `UnionCaseInfo` by tag.
/// 3. Decode the case's payload:
///    - 0 fields: `MakeUnion(case, [||])`
///    - 1 record field: parse the SAME lookup as that record (Pydantic flat style)
///    - other shapes: error in v1
and resolveUnionFromLookup
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (unionType: System.Type)
    (lookup: string -> obj option)
    : Result<obj, FieldError list> =
    match lookup discriminatorKey with
    | None ->
        Error [
            {
                path = discriminatorKey
                message = sprintf "missing discriminator '%s' for %s" discriminatorKey unionType.Name
            }
        ]
    | Some tagValue when backend.IsString tagValue ->
        let tag = backend.AsString tagValue
        let cases = FSharpType.GetUnionCases unionType

        match
            cases
            |> Array.tryFind (fun c -> tagOfCaseName c.Name = tag)
        with
        | None ->
            Error [
                {
                    path = discriminatorKey
                    message = sprintf "no case in %s matches discriminator value '%s'" unionType.Name tag
                }
            ]
        | Some caseInfo ->
            let caseFields = caseInfo.GetFields()

            match caseFields.Length with
            | 0 -> Ok(FSharpValue.MakeUnion(caseInfo, [||]))
            | 1 ->
                let payloadType = caseFields.[0].PropertyType

                if FSharpType.IsRecord payloadType then
                    resolveRecord backend registry keyTransform payloadType lookup
                    |> Result.map (fun payload -> FSharpValue.MakeUnion(caseInfo, [| payload |]))
                else
                    Error [
                        {
                            path = caseInfo.Name
                            message =
                                sprintf "union case %s has a non-record payload (%s); not supported in v1" caseInfo.Name payloadType.Name
                        }
                    ]
            | n ->
                Error [
                    {
                        path = caseInfo.Name
                        message =
                            sprintf "union case %s has %d positional fields; multi-field cases are not supported in v1" caseInfo.Name n
                    }
                ]
    | Some _ ->
        Error [
            {
                path = discriminatorKey
                message = sprintf "discriminator '%s' must be a string" discriminatorKey
            }
        ]

/// Coerce form: takes a backend-native JSON map (e.g., a nested union value
/// inside a record). Builds a key→obj lookup over the map and delegates.
and coerceUnion
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (unionType: System.Type)
    (jsonMap: obj)
    : Result<obj, string> =
    // resolveField applies keyTransform before calling the lookup, so the
    // key here is already in JSON form.
    let lookup (key: string) : obj option =
        if backend.ContainsKey(jsonMap, key) then
            Some(backend.Get(jsonMap, key))
        else
            None

    resolveUnionFromLookup backend registry keyTransform unionType lookup
    |> Result.mapError formatErrors

and resolveOptionalField
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (fi: System.Reflection.PropertyInfo)
    (name: string)
    (value: obj option)
    : Result<obj, FieldError> =
    match value with
    | None -> Ok(box None)
    | Some fv when backend.IsNull fv -> Ok(box None)
    | Some fv ->
        let innerType = getGenericInnerType fi.PropertyType

        coerce backend registry keyTransform innerType fv
        |> Result.map (buildOption innerType)
        |> Result.mapError (fun msg -> { path = name; message = msg })

and resolveRequiredField
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (fi: System.Reflection.PropertyInfo)
    (name: string)
    (value: obj option)
    : Result<obj, FieldError> =
    match value with
    | None ->
        Error {
            path = name
            message = sprintf "missing field (expected %s)" fi.PropertyType.Name
        }
    | Some fv when backend.IsNull fv ->
        Error {
            path = name
            message = sprintf "null value (expected %s)" fi.PropertyType.Name
        }
    | Some fv ->
        coerce backend registry keyTransform fi.PropertyType fv
        |> Result.mapError (fun msg -> { path = name; message = msg })

and resolveField
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (fi: System.Reflection.PropertyInfo)
    (lookup: string -> obj option)
    : Result<obj, FieldError> =
    // Use the case-rule-derived JSON key as the error path so messages
    // match what the user sees in their JSON, and so the path is consistent
    // across backends (BEAM lowercases `fi.Name`, Python and JS preserve
    // PascalCase — we route them all through the same transform).
    let name = keyTransform fi.Name
    let value = lookup name

    if isOptionType fi.PropertyType.FullName then
        resolveOptionalField backend registry keyTransform fi name value
    else
        resolveRequiredField backend registry keyTransform fi name value

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

(**
## buildRecordSchemaUntyped — pre-baked record decoder

Hoist all per-field reflection out of the per-decode hot path. At
codec-construction time we walk `FSharpType.GetRecordFields` once and
materialize per-field `jsonKeys`, `isOpts`, `innerTypes`, and `typeNames`
arrays. The returned closure does no reflection or string transformation
per call — it indexes the pre-baked arrays and dispatches into `coerce`.

`coerce` is still called per field per decode (since the JsonValue is
only known then), but the keyTransform / option-detection / inner-type
resolution work is amortized.

This is the single biggest decode-side perf lever — each layer it
removes from the hot path used to be paying string-allocation +
reflection-call per field per decode.
*)
// Not `private`: `auto<'T>` is `inline` and will inline calls to this helper
// at consumer call sites, which would fail accessibility checks against a
// private binding. Treat it as internal-use anyway — the module signature
// is the API surface.
let buildRecordSchemaUntyped
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (recordType: System.Type)
    : (string -> obj option) -> Result<obj, FieldError list>
    =
    let fields = FSharpType.GetRecordFields recordType
    let n = fields.Length
    let jsonKeys = Array.init n (fun i -> keyTransform fields.[i].Name)

    let isOpts =
        Array.init n (fun i -> isOptionType fields.[i].PropertyType.FullName)

    let innerTypes =
        Array.init n (fun i ->
            if isOpts.[i] then
                getGenericInnerType fields.[i].PropertyType
            else
                fields.[i].PropertyType)
    // `typeNames` is read only on the error path — we still pay one
    // allocation per array entry at construction time but save the
    // PropertyType.Name access per decode.
    let typeNames = Array.init n (fun i -> innerTypes.[i].Name)

    fun lookup ->
        let values = Array.zeroCreate<obj> n
        let mutable errs: FieldError list = []

        for i = 0 to n - 1 do
            let key = jsonKeys.[i]
            let value = lookup key
            let innerType = innerTypes.[i]

            let result: Result<obj, FieldError> =
                if isOpts.[i] then
                    match value with
                    | None -> Ok(box None)
                    | Some fv when backend.IsNull fv -> Ok(box None)
                    | Some fv ->
                        match coerce backend registry keyTransform innerType fv with
                        | Ok v -> Ok(buildOption innerType v)
                        | Error msg -> Error { path = key; message = msg }
                else
                    match value with
                    | None ->
                        Error {
                            path = key
                            message = sprintf "missing field (expected %s)" typeNames.[i]
                        }
                    | Some fv when backend.IsNull fv ->
                        Error {
                            path = key
                            message = sprintf "null where %s was required" typeNames.[i]
                        }
                    | Some fv ->
                        match coerce backend registry keyTransform innerType fv with
                        | Ok v -> Ok v
                        | Error msg -> Error { path = key; message = msg }

            match result with
            | Ok v -> values.[i] <- v
            | Error e -> errs <- e :: errs

        if List.isEmpty errs then
            Ok(FSharpValue.MakeRecord(recordType, values))
        else
            Error(List.rev errs)

let inline auto<'T> (backend: IJsonBackend) (registry: CodecRegistry) (keyTransform: string -> string) : Schema<'T> =
    let typ = typeof<'T>

    // Top-level dispatch: records → field-by-field via pre-baked schema;
    // tagged DUs → discriminator (case is value-dependent, can't pre-bake);
    // anything else is rejected (the user shouldn't reach `auto<'T>` for a
    // primitive type — that's `Codec.int` etc.).
    if FSharpType.IsRecord typ then
        // Pre-bake once per call site (which is once per codec, given that
        // `auto<'T>` is `inline`). The closure below does no reflection.
        let recordSchema = buildRecordSchemaUntyped backend registry keyTransform typ

        fun (lookup: string -> obj option) -> recordSchema lookup |> Result.map unbox<'T>
    elif FSharpType.IsUnion typ then
        fun (lookup: string -> obj option) ->
            resolveUnionFromLookup backend registry keyTransform typ lookup
            |> Result.map unbox<'T>
    else
        let badTypeError = [
            {
                path = ""
                message = sprintf "auto<%s> requires a record or discriminated-union type" typ.Name
            }
        ]

        fun _ -> Error badTypeError

// ============================================================================
// Adapters
// ============================================================================

(**
## Adapters

Convert various source formats to the `string -> obj option` lookup.
*)

/// Adapt a Map<string, string> (e.g., ToolCall.input from LLM). Each value
/// is the raw F# string — `coerce` recognises it via `backend.IsString`
/// and dispatches into the string-target arm of the giant primitive
/// pattern (which can also coerce to int / float / bool).
let stringMapAdapter (map: Map<string, string>) (key: string) : obj option =
    match Map.tryFind key map with
    | Some v -> Some(box v)
    | None -> None

/// Adapt a backend-native JSON map (e.g., parsed via `IJsonBackend.Parse`).
/// Returns the raw native value (Erlang binary, Python str, JS string, .NET
/// `JsonValue` case, …); the schema layer dispatches via `IJsonBackend.IsX`
/// / `AsX` rather than pattern-matching `JsonValue`.
let jsonMapAdapter (backend: IJsonBackend) (map: obj) (key: string) : obj option =
    if backend.ContainsKey(map, key) then
        Some(backend.Get(map, key))
    else
        None

// ============================================================================
// Convenience Functions
// ============================================================================

/// Validate from a string map (ToolCall.input from LLM). Uses an empty codec
/// registry and the identity key transform — keys in the input are treated
/// as the F# field names verbatim.
let inline validateMap<'T> (backend: IJsonBackend) (map: Map<string, string>) : Result<'T, FieldError list> =
    (auto<'T> backend emptyRegistry lowerFirstTransform) (stringMapAdapter map)

/// Validate from a string map with a custom codec registry.
let inline validateMapWith<'T> (backend: IJsonBackend) (registry: CodecRegistry) (map: Map<string, string>) : Result<'T, FieldError list> =
    (auto<'T> backend registry lowerFirstTransform) (stringMapAdapter map)

/// Validate from a backend-native JSON map. Uses an empty codec registry and
/// the identity key transform — JSON keys are treated as F# field names.
let inline validateJson<'T> (backend: IJsonBackend) (map: obj) : Result<'T, FieldError list> =
    (auto<'T> backend emptyRegistry lowerFirstTransform) (jsonMapAdapter backend map)

/// Validate from a backend-native JSON map with a custom codec registry.
let inline validateJsonWith<'T> (backend: IJsonBackend) (registry: CodecRegistry) (map: obj) : Result<'T, FieldError list> =
    (auto<'T> backend registry lowerFirstTransform) (jsonMapAdapter backend map)

/// Dump a record to a backend-native JSON map (e.g., for inter-process messaging).
let inline dump<'T> (backend: IJsonBackend) (record: 'T) : obj =
    let typ = typeof<'T>
    let fields = FSharpType.GetRecordFields typ
    let values = FSharpValue.GetRecordFields(box record)

    // BEAM lowercases F# field names in reflection; Python and JS preserve
    // PascalCase. Going through Schema's identity transform here would mean
    // dump produces different keys per backend — confusing and not portable.
    // Hardcode the canonical-PascalCase round-trip so the dumped map's keys
    // are stable across all targets.
    let normalize (n: string) : string =
        // PascalCase → LowerFirst-ish. Match what BEAM already produces.
        if n.Length = 0 then
            n
        else
            string (System.Char.ToLowerInvariant n.[0])
            + n.[1..]

    Array.zip fields values
    |> Array.fold
        (fun acc (fi, v) ->
            let key = normalize fi.Name

            if isOptionType fi.PropertyType.FullName then
                match extractOption v with
                | Some inner -> backend.Put(acc, key, inner)
                | None -> acc
            else
                backend.Put(acc, key, v))
        (backend.NewMap())
