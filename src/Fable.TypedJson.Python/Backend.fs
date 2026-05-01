(**
# PythonBackend — IJsonBackend implementation for the Python target

Wraps `Fable.Python.Json` (Python's `json` module) and uses Python's
native `dict` / `list` / `int` / `float` / `bool` / `str` / `None` for the
map abstraction.

adr: a Python dict is the native parsed-JSON map; `json.loads` returns
     nested dicts/lists/primitives, which is exactly what `JsonValue`
     erases to.
adr: `Put` mutates the dict in place (Pythonic) and returns the same
     reference; functional callers build fresh dicts via `NewMap` first.
adr: where possible we delegate to `Fable.Core.PyInterop` and
     `Fable.Python.Builtins` instead of raw `[<Emit>]` strings. The
     remaining emits (the type-reference values like `int`/`float`/...,
     the `None` singleton, and the `in` containment operator) aren't
     exposed by Fable.Python today — see issue tracking the gap.
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

// Native int / float wrapping at the read boundary.
// `int v` / `float v` in F# compile to `int32(v)` / `float64(v)` on the
// Fable Python target, which is what the erased `JInt n` / `JFloat f`
// patterns expect (`isinstance(_, int32)` / `isinstance(_, float64)`).
let private wrapAsInt32 (v: obj) : obj = box (int (unbox<int> v))
let private wrapAsFloat64 (v: obj) : obj = box (float (unbox<float> v))

let private wrapPrimitive (v: obj) : obj =
    // Bool subclasses int in Python, so check it first.
    if pyInstanceof v pyBool then v
    elif pyInstanceof v pyInt then wrapAsInt32 v
    elif pyInstanceof v pyFloat then wrapAsFloat64 v
    else v

type private PythonBackendImpl() =
    interface IJsonBackend with
        member _.NewMap() = pyEmptyDict ()

        member _.ContainsKey(map, key) = contains map key

        member _.Get(map, key) = wrapPrimitive (map?(key))

        member _.Put(map, key, value) =
            map?(key) <- value
            map

        member _.ParseRaw(jsonStr) = json.loads jsonStr

        member _.Stringify(value) = Json.dumps value

        // Type tests against the actual Python builtin types so values from
        // `json.loads` (native int/str/etc.) dispatch correctly. Bool subclasses
        // int in Python, so the int test excludes bools explicitly.
        member _.IsString(value) = pyInstanceof value pyStr

        member _.IsInt(value) =
            pyInstanceof value pyInt
            && not (pyInstanceof value pyBool)

        member _.IsFloat(value) = pyInstanceof value pyFloat
        member _.IsBool(value) = pyInstanceof value pyBool
        member _.IsNull(value) = emitPyExpr value "$0 is None"
        member _.IsArray(value) = pyInstanceof value pyList
        member _.IsMap(value) = pyInstanceof value pyDict
        member _.ArrayLength(arr) = builtins.len arr
        member _.ArrayAt(arr, i) = wrapPrimitive (arr?(i))
        // F# `obj list` is FSharpList on Python (cons cells with __slots__).
        // `json.dumps` would render that as a record-like dict, not a JSON
        // array. Convert to a native Python list via `list(xs)`.
        member _.BuildArray(items) = pyListOf items

        member _.Null = pyNone

/// Singleton PythonBackend instance — pass to `TypedJson.auto` /
/// `Schema.validateJson`, or use the convenience layer in
/// `Fable.TypedJson.Python.Json` which has it pre-applied.
let python: IJsonBackend = PythonBackendImpl() :> IJsonBackend
