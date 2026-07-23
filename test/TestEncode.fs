(**
# TestEncode — Tests for the Encode module

Tests that Encode.object, Encode.string, etc. build values that round-trip
through the backend's JSON serializer correctly.
*)

module Fable.TypedJson.Tests.Encode

open Fable.TypedJson.Testing
open Fable.TypedJson.Json
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

#if PYTHON
open Fable.TypedJson.Python.Json

let backend = python
#else
#if JS
open Fable.TypedJson.JS.Json

let backend = js
#else
#if DOTNET
open Fable.TypedJson.DotNet.Json

let backend = dotnet
#else
open Fable.TypedJson.Beam.Json

let backend = beam
#endif
#endif
#endif


let tests =
    testList (
        "Encode",
        [
            test (
                "encode simple object",
                fun _ ->
                    let json =
                        Encode.object [ "name", Encode.string "Alice"; "age", Encode.int 30 ]
                        |> Encode.toJson

                    let parsed = parseRaw json
                    let name = getString backend parsed "name"
                    let age = getInt backend parsed "age"
                    assertThat name (isEqualTo "Alice")
                    assertThat age (isEqualTo 30)
            )
            test (
                "encode nested object",
                fun _ ->
                    let json =
                        Encode.object [
                            "user", Encode.object [ "name", Encode.string "Bob"; "active", Encode.bool true ]
                        ]
                        |> Encode.toJson

                    let parsed = parseRaw json
                    let user = backend.Get(parsed, "user")
                    let name = getString backend user "name"
                    assertThat name (isEqualTo "Bob")
            )
            test (
                "encode list of strings",
                fun _ ->
                    let json =
                        Encode.object [ "tags", Encode.list Encode.string [ "a"; "b"; "c" ] ]
                        |> Encode.toJson

                    // Round-trip via the backend confirms the list is well-formed JSON.
                    let parsed = parseRaw json

                    assertThat (backend.IsArray(backend.Get(parsed, "tags"))) isTrue
            )
            test (
                "encode optional some",
                fun _ ->
                    let json =
                        Encode.object [ "value", Encode.optional Encode.int (Some 42) ]
                        |> Encode.toJson

                    let parsed = parseRaw json
                    let value = getInt backend parsed "value"
                    assertThat value (isEqualTo 42)
            )
            test (
                "encode optional none",
                fun _ ->
                    let json =
                        Encode.object [ "value", Encode.optional Encode.int None ]
                        |> Encode.toJson

                    let parsed = parseRaw json
                    // Each backend's JSON-null sentinel differs (BEAM: `null` atom from jsx,
                    // Python: `None`). `IsNull` abstracts the comparison.
                    assertThat (backend.IsNull(backend.Get(parsed, "value"))) isTrue
            )
            test (
                "encode handles special characters",
                fun _ ->
                    let json =
                        Encode.object [ "text", Encode.string "hello \"world\"\nnewline" ]
                        |> Encode.toJson

                    let parsed = parseRaw json
                    let text = getString backend parsed "text"
                    assertThat text (isEqualTo "hello \"world\"\nnewline")
            )
            test (
                "encode float",
                fun _ ->
                    let json =
                        Encode.object [ "temp", Encode.float 22.5 ]
                        |> Encode.toJson

                    let parsed = parseRaw json
                    let temp = getFloat backend parsed "temp"
                    assertThat temp (isEqualTo 22.5)
            )
            test (
                "encode bool",
                fun _ ->
                    let json =
                        Encode.object [ "active", Encode.bool true; "deleted", Encode.bool false ]
                        |> Encode.toJson

                    let parsed = parseRaw json
                    let active = getBool backend parsed "active"
                    let deleted = getBool backend parsed "deleted"
                    assertThat active isTrue
                    assertThat deleted isFalse
            )
            test (
                "encode raw pre-encoded json",
                fun _ ->
                    let inner = """{"x":1}"""

                    let json =
                        Encode.object [ "data", Encode.raw inner ]
                        |> Encode.toJson

                    let parsed = parseRaw json
                    let data = backend.Get(parsed, "data")
                    let x = getInt backend data "x"
                    assertThat x (isEqualTo 1)
            )
        ]
    )
