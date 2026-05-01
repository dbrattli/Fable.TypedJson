(**
# TestEncode — Tests for the Encode module

Tests that Encode.object, Encode.string, etc. build values that round-trip
through the backend's JSON serializer correctly.
*)

module Fable.TypedJson.Tests.Encode

open Fable.TypedJson.Testing
open Fable.TypedJson.Json

#if PYTHON
open Fable.TypedJson.Python.Json

let backend = python
#else
open Fable.TypedJson.Beam.Json

let backend = beam
#endif


[<Fact>]
let ``test encode simple object`` () =
    let json =
        Encode.object [ "name", Encode.string "Alice"; "age", Encode.int 30 ]
        |> Encode.toJson

    let parsed = parseRaw json
    let name = unbox<string> (backend.Get(parsed, "name"))
    let age = unbox<int> (backend.Get(parsed, "age"))
    name |> equal "Alice"
    age |> equal 30

[<Fact>]
let ``test encode nested object`` () =
    let json =
        Encode.object [
            "user", Encode.object [ "name", Encode.string "Bob"; "active", Encode.bool true ]
        ]
        |> Encode.toJson

    let parsed = parseRaw json
    let user = backend.Get(parsed, "user")
    let name = unbox<string> (backend.Get(user, "name"))
    name |> equal "Bob"

[<Fact>]
let ``test encode list of strings`` () =
    let json =
        Encode.object [ "tags", Encode.list Encode.string [ "a"; "b"; "c" ] ]
        |> Encode.toJson

    // Round-trip via the backend confirms the list is well-formed JSON.
    let parsed = parseRaw json

    backend.IsArray(backend.Get(parsed, "tags"))
    |> equal true

[<Fact>]
let ``test encode optional some`` () =
    let json =
        Encode.object [ "value", Encode.optional Encode.int (Some 42) ]
        |> Encode.toJson

    let parsed = parseRaw json
    let value = unbox<int> (backend.Get(parsed, "value"))
    value |> equal 42

[<Fact>]
let ``test encode optional none`` () =
    let json =
        Encode.object [ "value", Encode.optional Encode.int None ]
        |> Encode.toJson

    let parsed = parseRaw json
    // Each backend's JSON-null sentinel differs (BEAM: `null` atom from jsx,
    // Python: `None`). `IsNull` abstracts the comparison.
    backend.IsNull(backend.Get(parsed, "value"))
    |> equal true

[<Fact>]
let ``test encode handles special characters`` () =
    let json =
        Encode.object [ "text", Encode.string "hello \"world\"\nnewline" ]
        |> Encode.toJson

    let parsed = parseRaw json
    let text = unbox<string> (backend.Get(parsed, "text"))
    text |> equal "hello \"world\"\nnewline"

[<Fact>]
let ``test encode float`` () =
    let json =
        Encode.object [ "temp", Encode.float 22.5 ]
        |> Encode.toJson

    let parsed = parseRaw json
    let temp = unbox<float> (backend.Get(parsed, "temp"))
    temp |> equal 22.5

[<Fact>]
let ``test encode bool`` () =
    let json =
        Encode.object [ "active", Encode.bool true; "deleted", Encode.bool false ]
        |> Encode.toJson

    let parsed = parseRaw json
    let active = unbox<bool> (backend.Get(parsed, "active"))
    let deleted = unbox<bool> (backend.Get(parsed, "deleted"))
    active |> equal true
    deleted |> equal false

[<Fact>]
let ``test encode raw pre-encoded json`` () =
    let inner = """{"x":1}"""

    let json =
        Encode.object [ "data", Encode.raw inner ]
        |> Encode.toJson

    let parsed = parseRaw json
    let data = backend.Get(parsed, "data")
    let x = unbox<int> (backend.Get(data, "x"))
    x |> equal 1
