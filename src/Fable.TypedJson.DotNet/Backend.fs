(**
# DotNetBackend — IJsonBackend implementation for native .NET (System.Text.Json)

Wraps `System.Text.Json` (`JsonDocument` for parse, `Utf8JsonWriter` for
stringify) and uses `Dictionary<string, JsonValue>` + `JsonValue[]` for the
map / array abstraction.

decision: stores actual `JsonValue` cases — native CLR execution retains the tagged DU instead of target-native JSON terms
decision: mutates `Put` under linear ownership — avoids dictionary copies without exposing the prior accumulator
decision: boxes every parsed root as `JsonValue` — map operations and recursive fields then share one runtime shape
*)

module Fable.TypedJson.DotNet.Backend

open System.Buffers
open System.Collections.Generic
open System.Text
open System.Text.Json
open Fable.TypedJson.Backend
open Fable.TypedJson.Schema

// `JsonValue` collides with `System.Text.Json.Nodes.JsonValue`; we don't
// use the STJ DOM type, so no alias is needed. `JsonValue` below always
// refers to `Fable.TypedJson.Schema.JsonValue`.

// ----------------------------------------------------------------------------
// Helpers
// ----------------------------------------------------------------------------

/// Unwrap a boxed `JMap` to its underlying `Dictionary<string, JsonValue>`.
/// Throws if the value is not a `JMap` — callers must gate via `IsMap` or
/// only pass values they constructed via `NewMap` / `Put`.
let private asDict (v: obj) : Dictionary<string, JsonValue> =
    match unbox<JsonValue> v with
    | JMap inner -> unbox<Dictionary<string, JsonValue>> inner
    | other -> failwithf "expected JMap, got %A" other

/// Unwrap a boxed `JArray` to its underlying `JsonValue[]`.
let private asArray (v: obj) : JsonValue[] =
    match unbox<JsonValue> v with
    | JArray inner -> unbox<JsonValue[]> inner
    | other -> failwithf "expected JArray, got %A" other

/// Wrap a raw boxed value (as produced by `Encode.string`, `Encode.int`, …,
/// or by recursive `Encode.object` / `Encode.list`) into the matching
/// `JsonValue` case. Order matters: `bool` is checked before `int` because
/// `System.Boolean` would otherwise be missed. `JsonValue` is checked first
/// so already-wrapped values pass through unchanged (e.g. `backend.Null`,
/// nested maps from `Encode.object`).
let private wrap (v: obj) : JsonValue =
    match v with
    | null -> JNull
    | :? JsonValue as jv -> jv
    | :? bool as b -> JBool b
    | :? int as n -> JInt n
    | :? int64 as n -> JInt(int n)
    | :? float as f -> JFloat f
    | :? single as f -> JFloat(float f)
    | :? string as s -> JString s
    | _ -> failwithf "Cannot wrap value of type %s as JsonValue" (v.GetType().FullName)

// ----------------------------------------------------------------------------
// Parse — JsonElement → JsonValue
// ----------------------------------------------------------------------------

let rec private elementToJsonValue (e: JsonElement) : JsonValue =
    match e.ValueKind with
    | JsonValueKind.Null -> JNull
    | JsonValueKind.True -> JBool true
    | JsonValueKind.False -> JBool false
    | JsonValueKind.String -> JString(e.GetString())
    | JsonValueKind.Number ->
        // Prefer Int32 when the number fits — matches BEAM/JS behavior where
        // whole-valued JSON numbers become integers, so `coerce` can hit the
        // `JInt`-target branches instead of going through `JFloat`-to-int.
        match e.TryGetInt32() with
        | true, n -> JInt n
        | false, _ -> JFloat(e.GetDouble())
    | JsonValueKind.Array ->
        let len = e.GetArrayLength()
        let arr = Array.zeroCreate<JsonValue> len
        let mutable i = 0

        for child in e.EnumerateArray() do
            arr.[i] <- elementToJsonValue child
            i <- i + 1

        JArray(box arr)
    | JsonValueKind.Object ->
        let dict = Dictionary<string, JsonValue>()

        for prop in e.EnumerateObject() do
            dict.[prop.Name] <- elementToJsonValue prop.Value

        JMap(box dict)
    | k -> failwithf "Unsupported JsonValueKind: %A" k

// ----------------------------------------------------------------------------
// Stringify — JsonValue → UTF-8 JSON via Utf8JsonWriter
// ----------------------------------------------------------------------------

let rec private writeJsonValue (w: Utf8JsonWriter) (jv: JsonValue) =
    match jv with
    | JNull -> w.WriteNullValue()
    | JString s -> w.WriteStringValue(s)
    | JInt n -> w.WriteNumberValue(n)
    | JFloat f -> w.WriteNumberValue(f)
    | JBool b -> w.WriteBooleanValue(b)
    | JMap inner ->
        let dict = unbox<Dictionary<string, JsonValue>> inner
        w.WriteStartObject()

        for KeyValue(k, v) in dict do
            w.WritePropertyName(k)
            writeJsonValue w v

        w.WriteEndObject()
    | JArray inner ->
        let arr = unbox<JsonValue[]> inner
        w.WriteStartArray()

        for v in arr do
            writeJsonValue w v

        w.WriteEndArray()

let private stringifyJsonValue (jv: JsonValue) : string =
    // ArrayBufferWriter avoids the MemoryStream → ToArray copy → GetString
    // triple-allocation: write directly into a pooled-style growable buffer
    // and decode the written span in one pass.
    let bw = ArrayBufferWriter<byte>()
    use writer = new Utf8JsonWriter(bw)
    writeJsonValue writer jv
    writer.Flush()
    Encoding.UTF8.GetString(bw.WrittenSpan)

// ----------------------------------------------------------------------------
// IJsonBackend implementation
// ----------------------------------------------------------------------------

type private DotNetBackendImpl() =
    interface IJsonBackend with
        // NewMap returns a boxed JMap so every downstream Put / Get / IsMap
        // sees the same "boxed JsonValue" shape as the recursive case.
        member _.NewMap() =
            box (JMap(box (Dictionary<string, JsonValue>())))

        member _.ContainsKey(map, key) =
            match map with
            | :? JsonValue as JMap inner -> (unbox<Dictionary<string, JsonValue>> inner).ContainsKey(key)
            | _ -> false

        member _.Get(map, key) = box ((asDict map).[key])

        member _.Put(map, key, value) =
            // Mutating-and-returning the same boxed JMap is sound because
            // the core only uses Put inside fold patterns; see file header.
            (asDict map).[key] <- wrap value
            map

        member _.ParseRaw(json) =
            use doc = JsonDocument.Parse(json: string)
            box (elementToJsonValue doc.RootElement)

        member _.Stringify(value) = stringifyJsonValue (wrap value)

        // `backend.Null` is the value `Encode.optional` writes for `None`.
        // Storing it as `JNull` lets `Stringify` emit JSON null without an
        // extra type test (the wrap helper passes JsonValue through).
        member _.Null = box JNull

        // Type tests handle BOTH boxed `JsonValue` cases (post-parse, decode
        // hot path) AND raw native primitives (`stringMapAdapter` and the
        // `Encode.string`/`Encode.int` helpers pass `box "x"` / `box 42`
        // directly). Without the raw-value branch, decoders driven by the
        // string-map adapter would see `IsString` return false for the
        // strings actually in the map and fail with "<unknown>" errors.
        member _.IsString(value) =
            match value with
            | :? string -> true
            | :? JsonValue as JString _ -> true
            | _ -> false

        member _.IsInt(value) =
            match value with
            | :? bool -> false
            | :? int -> true
            | :? JsonValue as JInt _ -> true
            | _ -> false

        member _.IsFloat(value) =
            match value with
            | :? float -> true
            | :? JsonValue as JFloat _ -> true
            | _ -> false

        member _.IsBool(value) =
            match value with
            | :? bool -> true
            | :? JsonValue as JBool _ -> true
            | _ -> false

        member _.IsNull(value) =
            match value with
            | null -> true
            | :? JsonValue as JNull -> true
            | _ -> false

        member _.IsArray(value) =
            match value with
            | :? JsonValue as JArray _ -> true
            | _ -> false

        member _.IsMap(value) =
            match value with
            | :? JsonValue as JMap _ -> true
            | _ -> false

        // Typed accessors — symmetric to the IsX checks: handle both the
        // `JsonValue` case wrapping (parsed values) and the raw native type
        // (string-map adapter / encode helpers).
        member _.AsString(value) =
            match value with
            | :? string as s -> s
            | :? JsonValue as JString s -> s
            | other -> failwithf "AsString called on non-string: %A" other

        member _.AsInt(value) =
            match value with
            | :? int as n -> n
            | :? JsonValue as JInt n -> n
            | other -> failwithf "AsInt called on non-int: %A" other

        member _.AsFloat(value) =
            match value with
            | :? float as f -> f
            | :? JsonValue as JFloat f -> f
            | other -> failwithf "AsFloat called on non-float: %A" other

        member _.AsBool(value) =
            match value with
            | :? bool as b -> b
            | :? JsonValue as JBool b -> b
            | other -> failwithf "AsBool called on non-bool: %A" other

        // ArrayLength/ArrayAt accept both a `JArray`-wrapped `JsonValue[]`
        // (the decode path, post-parse) and a raw .NET `obj[]` (the encode
        // path, where the union plan calls these on boxed `caseValues` from
        // `FSharpValue.GetUnionFields`). The Fable
        // targets see both as the "native list" thanks to erasure; the
        // .NET shim has to handle them as distinct CLR types.
        member _.ArrayLength(arr) =
            match arr with
            | :? JsonValue as JArray inner -> (unbox<JsonValue[]> inner).Length
            | :? System.Array as a -> a.Length
            | _ -> failwithf "expected array, got %A" arr

        member _.ArrayAt(arr, i) =
            match arr with
            | :? JsonValue as JArray inner -> box ((unbox<JsonValue[]> inner).[i])
            | :? System.Array as a -> a.GetValue(i)
            | _ -> failwithf "expected array, got %A" arr

        member _.BuildArray(items) =
            let arr = items |> List.map wrap |> List.toArray
            box (JArray(box arr))

/// Singleton DotNetBackend instance — pass to TypedJson.auto / Schema.validateJson.
let dotnet: IJsonBackend = DotNetBackendImpl() :> IJsonBackend
