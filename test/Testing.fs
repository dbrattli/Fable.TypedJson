(**
# Testing — Test utilities for Fable.TypedJson

Cross-backend test helpers. Assertions and the runner come from Scriptorium
(Nib + Quill), which compiles to every Fable target; what is left here is the
glue the suite needs to read values back out of a backend's JSON map.

The `getString`/`getInt`/`getFloat`/`getBool` helpers go through
`IJsonBackend.IsX` / `AsX` so they work on raw native values returned by
`Get` (Erlang binary, Python str, JS string, .NET `JsonValue` case)
without assuming a particular wrapping. This mirrors the production
schema layer, which switched off pattern-matching `JsonValue` for the
same portability reason.

adr: assertions come from Nib (`assertThat` + `isEqualTo`), not a local `equal` — one failure format on every target
invariant: helpers here dispatch through `IJsonBackend.IsX` / `AsX`, never on a backend's native representation
*)

module Fable.TypedJson.Testing

open Fable.TypedJson.Backend

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
