(**
# BeamBackend — IJsonBackend implementation for the Erlang/BEAM target

Wraps `Fable.Beam.Maps` (BEAM `maps` module) and `Fable.Beam.Jsx.Jsx`
(jsx JSON library) to implement `Fable.TypedJson.Backend.IJsonBackend`.

decision: requests jsx `returnMaps` output — nested JSON objects stay native `BeamMap<string, obj>` values
*)

module Fable.TypedJson.Beam.Backend

open Fable.Core
open Fable.Beam.Maps
open Fable.Beam.Jsx.Jsx
open Fable.TypedJson.Backend

// Erlang type-guard primitives — used for the IJsonBackend type-test methods.
[<Emit("is_binary($0)")>]
let private isErlBinary (v: obj) : bool = nativeOnly

[<Emit("is_integer($0)")>]
let private isErlInteger (v: obj) : bool = nativeOnly

[<Emit("is_float($0)")>]
let private isErlFloat (v: obj) : bool = nativeOnly

[<Emit("is_boolean($0)")>]
let private isErlBoolean (v: obj) : bool = nativeOnly

[<Emit("is_atom($0) andalso $0 =:= null")>]
let private isErlNull (v: obj) : bool = nativeOnly

[<Emit("is_list($0)")>]
let private isErlList (v: obj) : bool = nativeOnly

[<Emit("is_map($0)")>]
let private isErlMap (v: obj) : bool = nativeOnly

// `BuildArray` emits a plain Erlang list; Fable 5.11's
// `FSharpValue.GetUnionFields` returns process-dict array refs for `obj[]` case
// values. `fable_utils:to_list` normalizes both representations.
// assumption: sequence inputs are plain Erlang lists or Fable process-dict array refs
[<Emit("length(fable_utils:to_list($0))")>]
let private erlListLength (l: obj) : int = nativeOnly

[<Emit("lists:nth($1 + 1, fable_utils:to_list($0))")>]
let private erlListAt (l: obj) (i: int) : obj = nativeOnly

// F# `obj list` IS an Erlang list at runtime on Fable BEAM, so the box is
// effectively the identity here. Kept explicit for documentation and parity
// with the Python backend (where the conversion is non-trivial).
[<Emit("$0")>]
let private erlListFromFSharpList (xs: obj list) : obj = nativeOnly

// The Erlang atom `null` — jsx serializes this as JSON null. Crucially this
// is NOT the same as F# `null` (which Fable BEAM compiles to the `undefined`
// atom and jsx then serializes as the JSON string `"undefined"`).
[<Emit("null")>]
let private erlNull: obj = nativeOnly

type private BeamBackendImpl() =
    interface IJsonBackend with
        member _.NewMap() = box (maps.new_ ())

        member _.ContainsKey(map, key) = maps.is_key (key, unbox map)

        member _.Get(map, key) =
            maps.get (key, unbox<BeamMap<string, obj>> map)

        member _.Put(map, key, value) =
            box (maps.put (key, value, unbox<BeamMap<string, obj>> map))

        member _.ParseRaw(json) = box (jsx.decode (json, [ returnMaps ]))

        member _.Stringify(value) = jsx.encode (unbox value)

        // BEAM type tests — Erlang is_* type guards. Note: is_boolean must be checked
        // before is_atom-style checks since booleans are atoms (`true`, `false`).
        member _.IsString(value) = isErlBinary value

        member _.IsInt(value) =
            isErlInteger value && not (isErlBoolean value)

        member _.IsFloat(value) = isErlFloat value
        member _.IsBool(value) = isErlBoolean value
        member _.IsNull(value) = isErlNull value
        member _.IsArray(value) = isErlList value
        member _.IsMap(value) = isErlMap value

        // Parsed values are already native Erlang terms. Fable erases these
        // typed unboxes, so the accessors are runtime identities on BEAM.
        member _.AsString(value) = unbox<string> value
        member _.AsInt(value) = unbox<int> value
        member _.AsFloat(value) = unbox<float> value
        member _.AsBool(value) = unbox<bool> value
        member _.ArrayLength(arr) = erlListLength arr
        member _.ArrayAt(arr, i) = erlListAt arr i
        member _.BuildArray(items) = erlListFromFSharpList items

        member _.Null = erlNull

/// Singleton BeamBackend instance — pass to TypedJson.auto / Schema.validateJson.
let beam: IJsonBackend = BeamBackendImpl() :> IJsonBackend
