(**
# JSBackend -- IJsonBackend implementation for the JavaScript target

Wraps `JSON.parse` / `JSON.stringify` and uses JavaScript's native object,
array, number, string, boolean, and null for the map abstraction.

adr: a plain JS object (`{}`) is the native parsed-JSON map; `JSON.parse`
     returns nested objects/arrays/primitives, which is exactly what
     `JsonValue` erases to.
adr: `Put` is non-mutating -- builds a fresh object via spread so the
     core layer's "fold map updates over an accumulator" pattern works
     functionally. Mutation would alias the accumulator across folds.
adr: numbers in JS are all double -- `IsInt` uses `Number.isInteger` to
     distinguish whole-valued numbers from fractional ones, matching the
     coercion expectations of the core (`JString "42"` -> `int 42`).
adr: prefer `Fable.Core.JS` and `Fable.Core.JsInterop` over raw `[<Emit>]`
     where possible. Only the spread literal (`Put`) and the `null`
     sentinel still need raw emit -- everything else routes through
     blessed bindings.
*)

module Fable.TypedJson.JS.Backend

open Fable.Core
open Fable.Core.JsInterop
open Fable.TypedJson.Backend

// Object-spread + computed-key insertion isn't expressible in
// Fable.Core.JsInterop, so this stays as raw emit. Keeping it functional
// (non-mutating) matches the IJsonBackend.Put contract.
[<Emit("({...$0, [$1]: $2})")>]
let private jsSpreadPut (map: obj) (key: string) (value: obj) : obj = nativeOnly

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

        member _.Put(map, key, value) = jsSpreadPut map key value

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
