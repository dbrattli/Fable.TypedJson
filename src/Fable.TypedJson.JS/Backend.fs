(**
# JSBackend -- IJsonBackend implementation for the JavaScript target

Wraps `JSON.parse` / `JSON.stringify` and uses JavaScript's native object,
array, number, string, boolean, and null for the map abstraction.

decision: uses native JS objects, arrays, and primitives — `JSON.parse` output needs no representation conversion
decision: mutates `Put` under linear ownership — avoiding a full object spread per key keeps object building O(n)
decision: classifies whole-valued doubles with `Number.isInteger` — JavaScript has no distinct integer number type
decision: prefers Fable interop bindings over raw emits — target-language strings remain limited to missing APIs
*)

module Fable.TypedJson.JS.Backend

open Fable.Core
open Fable.Core.JsInterop
open Fable.TypedJson.Backend

// Assign-and-return is not expressible in Fable.Core.JsInterop. Linear
// ownership permits this emit to mutate without exposing aliases.
[<Emit("($0[$1] = $2, $0)")>]
let private jsPut (map: obj) (key: string) (value: obj) : obj = nativeOnly

// `Fable.Core.JS.NumberConstructor` exposes `isNaN` and `isSafeInteger`
// but not `isInteger` (yet). Bind it directly until upstream catches up.
[<Emit("Number.isInteger($0)")>]
let private isInteger (v: obj) : bool = nativeOnly

type private JSBackendImpl() =
    interface IJsonBackend with
        // `createEmpty<obj>` lowers to `{}`.
        member _.NewMap() = createEmpty<obj>

        // `key in map` — `JsInterop.jsIn` is the blessed wrapper.
        member _.ContainsKey(map, key) = jsIn key map

        // `JsInterop.(?)` lowers to `map[key]`.
        member _.Get(map, key) = map?(key)

        member _.Put(map, key, value) = jsPut map key value

        member _.ParseRaw(json) = JS.JSON.parse json

        member _.Stringify(value) = JS.JSON.stringify value

        // `JsInterop.jsTypeof` lowers to `typeof v`.
        member _.IsString(value) = jsTypeof value = "string"

        member _.IsInt(value) =
            jsTypeof value = "number" && isInteger value

        member _.IsFloat(value) =
            jsTypeof value = "number" && not (isInteger value)

        member _.IsBool(value) = jsTypeof value = "boolean"

        // `box value = null` works because Fable lowers F#-side `null` for
        // an `obj` reference to JS `null`. (Inside an F# function body —
        // contrast with a `[<Global>]` value binding, where F# `null` is
        // `undefined`.)
        member _.IsNull(value) = isNull value

        member _.IsArray(value) = JS.Constructors.Array.isArray value

        member _.IsMap(value) =
            jsTypeof value = "object"
            && not (isNull value)
            && not (JS.Constructors.Array.isArray value)

        // Typed accessors — JS strings, numbers, and booleans are native
        // primitives; the unbox at the F# level is a no-op at runtime.
        member _.AsString(value) = unbox<string> value
        member _.AsInt(value) = unbox<int> value
        member _.AsFloat(value) = unbox<float> value
        member _.AsBool(value) = unbox<bool> value

        // Dynamic property access via `?`. Cast the result through `unbox`
        // because `?` returns generic `'a` and `length` is `int`.
        member _.ArrayLength(arr) = unbox<int> arr?length
        member _.ArrayAt(arr, i) = arr?(i)

        // F# `obj list` on Fable's JS target is a linked-list structure (not
        // a JS array), which `JSON.stringify` would render as a record-like
        // object instead of a JSON array. Walk to a fresh JS array.
        member _.BuildArray(items) =
            // `List.toArray` produces a `obj[]` whose JS shape is a real
            // `Array` — exactly what JSON.stringify emits as a JSON array.
            box (List.toArray items)

        // F# `null` for the `obj` type lowers to JS `null` (not `undefined`)
        // on Fable's JavaScript backend — exactly the JSON-null sentinel
        // `JSON.stringify` emits as JSON null.
        member _.Null = null

/// Singleton JSBackend instance -- pass to TypedJson.auto / Schema.validateJson.
let js: IJsonBackend = JSBackendImpl() :> IJsonBackend
