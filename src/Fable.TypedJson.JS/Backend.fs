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
*)

module Fable.TypedJson.JS.Backend

open Fable.Core
open Fable.TypedJson.Backend

// JS native type tests via typeof / Number / Array / instanceof.
[<Emit("typeof $0 === 'string'")>]
let private isJsString (v: obj) : bool = nativeOnly

[<Emit("typeof $0 === 'number' && Number.isInteger($0)")>]
let private isJsInt (v: obj) : bool = nativeOnly

[<Emit("typeof $0 === 'number' && !Number.isInteger($0)")>]
let private isJsFloat (v: obj) : bool = nativeOnly

[<Emit("typeof $0 === 'boolean'")>]
let private isJsBool (v: obj) : bool = nativeOnly

[<Emit("$0 === null")>]
let private isJsNull (v: obj) : bool = nativeOnly

[<Emit("Array.isArray($0)")>]
let private isJsArray (v: obj) : bool = nativeOnly

[<Emit("typeof $0 === 'object' && $0 !== null && !Array.isArray($0)")>]
let private isJsObject (v: obj) : bool = nativeOnly

[<Emit("{}")>]
let private jsEmptyObject () : obj = nativeOnly

[<Emit("$1 in $0")>]
let private jsHasKey (map: obj) (key: string) : bool = nativeOnly

[<Emit("$0[$1]")>]
let private jsGet (map: obj) (key: string) : obj = nativeOnly

[<Emit("({...$0, [$1]: $2})")>]
let private jsPut (map: obj) (key: string) (value: obj) : obj = nativeOnly

[<Emit("JSON.parse($0)")>]
let private jsParse (json: string) : obj = nativeOnly

[<Emit("JSON.stringify($0)")>]
let private jsStringify (value: obj) : string = nativeOnly

[<Emit("$0.length")>]
let private jsArrayLength (arr: obj) : int = nativeOnly

[<Emit("$0[$1]")>]
let private jsArrayAt (arr: obj) (i: int) : obj = nativeOnly

// F# `obj list` on Fable's JS target is a linked-list structure (not a JS array),
// which `JSON.stringify` would render as a record-like object instead of a JSON
// array. Walk to a fresh JS array.
[<Emit("(() => { const r = []; let xs = $0; while (xs && xs.tail) { r.push(xs.head); xs = xs.tail; } return r; })()")>]
let private jsArrayFromFSharpList (xs: obj list) : obj = nativeOnly

[<Emit("null")>]
let private jsNull: obj = nativeOnly

type private JSBackendImpl() =
    interface IJsonBackend with
        member _.NewMap() = jsEmptyObject ()

        member _.ContainsKey(map, key) = jsHasKey map key

        member _.Get(map, key) = jsGet map key

        member _.Put(map, key, value) = jsPut map key value

        member _.ParseRaw(json) = jsParse json

        member _.Stringify(value) = jsStringify value

        member _.IsString(value) = isJsString value

        member _.IsInt(value) = isJsInt value

        member _.IsFloat(value) = isJsFloat value

        member _.IsBool(value) = isJsBool value

        member _.IsNull(value) = isJsNull value

        member _.IsArray(value) = isJsArray value

        member _.IsMap(value) = isJsObject value

        member _.ArrayLength(arr) = jsArrayLength arr

        member _.ArrayAt(arr, i) = jsArrayAt arr i

        member _.BuildArray(items) = jsArrayFromFSharpList items

        member _.Null = jsNull

/// Singleton JSBackend instance -- pass to TypedJson.auto / Schema.validateJson.
let js: IJsonBackend = JSBackendImpl() :> IJsonBackend
