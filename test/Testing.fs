(**
# Testing — Test utilities for Fable.TypedJson

Cross-backend test helpers. `equal` raises when expected ≠ actual so that
both BEAM (whose `Fable.Core.Testing.Assert.AreEqual` silently returns a
bool) and Python's pytest see real failures.

The `getString`/`getInt`/`getFloat`/`getBool` helpers route through
`unbox<JsonValue>` + pattern match. On Fable backends `JsonValue` is
`[<Erase>]` so the match compiles to a native type guard (zero cost); on
the .NET backend `JsonValue` is a real DU and the match dispatches the
DU case. Both paths give a portable way to extract a typed primitive
from `backend.Get` / `backend.ArrayAt` without assuming the value's
runtime shape.
*)

module Fable.TypedJson.Testing

open Fable.TypedJson.Backend
open Fable.TypedJson.Schema

type FactAttribute() =
    inherit System.Attribute()

let inline equal expected actual : unit =
    if not (LanguagePrimitives.GenericEquality expected actual) then
        failwithf "expected %A but got %A" expected actual

let inline notEqual expected actual : unit =
    if LanguagePrimitives.GenericEquality expected actual then
        failwithf "expected NOT %A but got %A" expected actual

// ----------------------------------------------------------------------------
// Backend-portable Get / ArrayAt extractors
// ----------------------------------------------------------------------------

let getString (backend: IJsonBackend) (map: obj) (key: string) : string =
    match unbox<JsonValue> (backend.Get(map, key)) with
    | JString s -> s
    | other -> failwithf "expected string at '%s', got %A" key other

let getInt (backend: IJsonBackend) (map: obj) (key: string) : int =
    match unbox<JsonValue> (backend.Get(map, key)) with
    | JInt n -> n
    | JFloat f -> int f
    | other -> failwithf "expected int at '%s', got %A" key other

let getFloat (backend: IJsonBackend) (map: obj) (key: string) : float =
    match unbox<JsonValue> (backend.Get(map, key)) with
    | JFloat f -> f
    | JInt n -> float n
    | other -> failwithf "expected float at '%s', got %A" key other

let getBool (backend: IJsonBackend) (map: obj) (key: string) : bool =
    match unbox<JsonValue> (backend.Get(map, key)) with
    | JBool b -> b
    | other -> failwithf "expected bool at '%s', got %A" key other

let arrayAtString (backend: IJsonBackend) (arr: obj) (i: int) : string =
    match unbox<JsonValue> (backend.ArrayAt(arr, i)) with
    | JString s -> s
    | other -> failwithf "expected string at [%d], got %A" i other
