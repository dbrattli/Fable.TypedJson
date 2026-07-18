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

// Fable's `Int32` and `Float64` Python classes — F# `int` / `float`
// values arrive boxed as instances of these, NOT as native Python `int`
// / `float`. (Despite the `.pyi` declaring `Int32(int)`, at runtime
// Fable's Int32 does NOT inherit from Python's int — confirmed via
// `isinstance(int32(42), int) is False`.) The schema-side `IsInt` /
// `IsFloat` therefore have to test against both shapes: the native
// type (for `json.loads` outputs and `stringMapAdapter` boxes) and
// Fable's wrapper (for F# `int`/`float` values handed in directly).
[<Emit("__import__(\"fable_library.core\", fromlist=[\"int32\"]).int32")>]
let private pyInt32: obj = nativeOnly

[<Emit("__import__(\"fable_library.core\", fromlist=[\"float64\"]).float64")>]
let private pyFloat64: obj = nativeOnly

// `dict()` and `list(...)` constructors aren't on Fable.Python.Builtins'
// `IExports` interface even though the type references above are. File an
// upstream addition — until then, emit directly.
[<Emit("dict()")>]
let private pyEmptyDict () : obj = nativeOnly

[<Emit("list($0)")>]
let private pyListOf (xs: obj) : obj = nativeOnly

// `key in dict` — Python's `in` is a syntactic operator with no Fable wrapper.
let private contains (map: obj) (key: string) : bool = emitPyExpr (key, map) "$0 in $1"

// Native int / float wrapping at the F# boundary.
// `int v` / `float v` in F# compile to `int32(v)` / `float64(v)` on the
// Fable Python target, which is what F# code expects when typed as `int`
// / `float`. Native Python ints from `json.loads` aren't `int32`, so we
// wrap when handing values back to the F# layer (in `AsInt` / `AsFloat`
// and when constructing user-facing `JsonValue` cases).
let private wrapAsInt32 (v: obj) : obj = box (int (unbox<int> v))
let private wrapAsFloat64 (v: obj) : obj = box (float (unbox<float> v))

type private PythonBackendImpl() =
    interface IJsonBackend with
        member _.NewMap() = pyEmptyDict ()

        member _.ContainsKey(map, key) = contains map key

        // Get returns the raw native value. The schema layer dispatches
        // via `backend.IsX` / `AsX`, which know to handle native Python
        // primitives; the previous Get-time wrap to `int32` / `float64`
        // existed only so the (now-removed) `JInt n` / `JFloat f` pattern
        // matches in the core hot path would compile to `isinstance(_,
        // int32)` checks. With pattern-matching out of the hot path, we
        // wrap on the way out (AsInt / AsFloat) instead.
        member _.Get(map, key) = map?(key)

        member _.Put(map, key, value) =
            map?(key) <- value
            map

        member _.ParseRaw(jsonStr) = json.loads jsonStr

        member _.Stringify(value) = Json.dumps value

        // Type tests recognise BOTH native Python primitives (from
        // `json.loads`, `stringMapAdapter`) AND Fable's `Int32` / `Float64`
        // wrappers (F# `int` / `float` literals constructed in F# code).
        // Bool subclasses Python int, so the int test excludes bools
        // explicitly. Fable's `Int32` does NOT subclass Python int, hence
        // the explicit second branch.
        member _.IsString(value) = pyInstanceof value pyStr

        member _.IsInt(value) =
            not (pyInstanceof value pyBool)
            && (pyInstanceof value pyInt
                || pyInstanceof value pyInt32)

        member _.IsFloat(value) =
            pyInstanceof value pyFloat
            || pyInstanceof value pyFloat64

        member _.IsBool(value) = pyInstanceof value pyBool
        member _.IsNull(value) = emitPyExpr value "$0 is None"
        member _.IsArray(value) = pyInstanceof value pyList
        member _.IsMap(value) = pyInstanceof value pyDict

        // Typed accessors. Native Python `str` / `bool` map straight through;
        // for `int` and `float` we go through the same int32/float64 wrapping
        // that `Get` does so the F# side sees the wrapped types its codecs
        // dispatch against (`isinstance(_, int32)` etc.).
        member _.AsString(value) = unbox<string> value
        member _.AsInt(value) = unbox<int> (wrapAsInt32 value)
        member _.AsFloat(value) = unbox<float> (wrapAsFloat64 value)
        member _.AsBool(value) = unbox<bool> value
        member _.ArrayLength(arr) = builtins.len arr
        // ArrayAt is symmetric to Get — return the raw native value, the
        // caller dispatches via IsX / AsX.
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
