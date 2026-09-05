(**
# PythonBackend — IJsonBackend implementation for the Python target

Wraps `Fable.Python.Json` (Python's `json` module) and uses Python's
native `dict` / `list` / `int` / `float` / `bool` / `str` / `None` for the
map abstraction.

decision: uses native Python dicts, lists, and primitives — `json.loads` output needs no representation conversion
decision: mutates `Put` under linear ownership — avoids copying while a fresh `NewMap` prevents caller aliasing
decision: prefers Fable interop bindings over raw emits — target-language strings remain limited to missing APIs
*)

module Fable.TypedJson.Python.Backend

open Fable.Core
open Fable.Core.PyInterop
open Fable.TypedJson.Backend
open Fable.Python.Builtins
open Fable.Python.Json

// ---------------------------------------------------------------------------
// Python type references and the `None` singleton.
// These are missing from Fable.Python; using `[<Global; Emit "..." >]` mirrors
// what Thoth.Json.Python does. Worth upstreaming a `Fable.Python.Types` module
// (or adding to Builtins) so every consumer doesn't redeclare them.
// ---------------------------------------------------------------------------

[<Global; Emit("str")>]
let private pyStr: obj = nativeOnly

[<Global; Emit("int")>]
let private pyInt: obj = nativeOnly

[<Global; Emit("float")>]
let private pyFloat: obj = nativeOnly

[<Global; Emit("bool")>]
let private pyBool: obj = nativeOnly

[<Global; Emit("list")>]
let private pyList: obj = nativeOnly

[<Global; Emit("dict")>]
let private pyDict: obj = nativeOnly

[<Global; Emit("None")>]
let private pyNone: obj = nativeOnly

// `dict()` and `list(...)` constructors aren't on Fable.Python.Builtins'
// `IExports` interface even though the type references above are. File an
// upstream addition — until then, emit directly.
[<Emit("dict()")>]
let private pyEmptyDict () : obj = nativeOnly

[<Emit("list($0)")>]
let private pyListOf (xs: obj) : obj = nativeOnly

// `key in dict` — Python's `in` is a syntactic operator with no Fable wrapper.
let private contains (map: obj) (key: string) : bool = emitPyExpr (key, map) "$0 in $1"

type private PythonBackendImpl() =
    interface IJsonBackend with
        member _.NewMap() = pyEmptyDict ()

        member _.ContainsKey(map, key) = contains map key

        // Get returns raw native values; the schema layer dispatches through
        // `backend.IsX` / `AsX`, so no representation conversion is needed.
        member _.Get(map, key) = map?(key)

        member _.Put(map, key, value) =
            map?(key) <- value
            map

        member _.ParseRaw(jsonStr) = json.loads jsonStr

        member _.Stringify(value) = Json.dumps value

        // Fable 5.16 represents F# int and float as native Python primitives.
        // Bool subclasses Python int, so the int test excludes bools explicitly.
        // decision: tests runtime types rather than Fable coercion helpers so parsed JSON and F# values share one path
        member _.IsString(value) = pyInstanceof value pyStr

        member _.IsInt(value) =
            not (pyInstanceof value pyBool)
            && pyInstanceof value pyInt

        member _.IsFloat(value) = pyInstanceof value pyFloat

        member _.IsBool(value) = pyInstanceof value pyBool
        member _.IsNull(value) = emitPyExpr value "$0 is None"
        member _.IsArray(value) = pyInstanceof value pyList
        member _.IsMap(value) = pyInstanceof value pyDict

        // Typed accessors map native Python primitives straight through.
        member _.AsString(value) = unbox<string> value
        member _.AsInt(value) = unbox<int> value
        member _.AsFloat(value) = unbox<float> value
        member _.AsBool(value) = unbox<bool> value
        member _.ArrayLength(arr) = builtins.len arr
        // ArrayAt is symmetric to Get — return the raw native value.
        member _.ArrayAt(arr, i) = arr?(i)
        // F# `obj list` is FSharpList on Python (cons cells with __slots__).
        // `json.dumps` would render that as a record-like dict, not a JSON
        // array. Convert to a native Python list via `list(xs)`.
        member _.BuildArray(items) = pyListOf items

        member _.Null = pyNone

/// Singleton PythonBackend instance — pass to `TypedJson.auto` /
/// `Schema.validateJson`, or use the convenience layer in
/// `Fable.TypedJson.Python.Json` which has it pre-applied.
let python: IJsonBackend = PythonBackendImpl() :> IJsonBackend
