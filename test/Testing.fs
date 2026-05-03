(**
# Testing — Test utilities for Fable.TypedJson

Cross-backend test helpers. `equal` raises when expected ≠ actual so that
both BEAM (whose `Fable.Core.Testing.Assert.AreEqual` silently returns a
bool) and Python's pytest see real failures.

The `getString`/`getInt`/`getFloat`/`getBool` helpers go through
`IJsonBackend.IsX` / `AsX` so they work on raw native values returned by
`Get` (Erlang binary, Python str, JS string, .NET `JsonValue` case)
without assuming a particular wrapping. This mirrors the production
schema layer, which switched off pattern-matching `JsonValue` for the
same portability reason.
*)

module Fable.TypedJson.Testing

open Fable.TypedJson.Backend

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
    let v = backend.Get(map, key)

    if backend.IsString v then
        backend.AsString v
    else
        failwithf "expected string at '%s', got %A" key v

let getInt (backend: IJsonBackend) (map: obj) (key: string) : int =
    let v = backend.Get(map, key)

    if backend.IsInt v then backend.AsInt v
    elif backend.IsFloat v then int (backend.AsFloat v)
    else failwithf "expected int at '%s', got %A" key v

let getFloat (backend: IJsonBackend) (map: obj) (key: string) : float =
    let v = backend.Get(map, key)

    if backend.IsFloat v then backend.AsFloat v
    elif backend.IsInt v then float (backend.AsInt v)
    else failwithf "expected float at '%s', got %A" key v

let getBool (backend: IJsonBackend) (map: obj) (key: string) : bool =
    let v = backend.Get(map, key)

    if backend.IsBool v then
        backend.AsBool v
    else
        failwithf "expected bool at '%s', got %A" key v

let arrayAtString (backend: IJsonBackend) (arr: obj) (i: int) : string =
    let v = backend.ArrayAt(arr, i)

    if backend.IsString v then
        backend.AsString v
    else
        failwithf "expected string at [%d], got %A" i v
