(**
# TestAuto — Tests for TypedJson.auto reflection-based codec

Tests that auto<'T> correctly decodes JSON into F# records
and encodes records back to JSON, using FSharp.Reflection.
*)

module Fable.TypedJson.Tests.Auto

open Fable.TypedJson.Testing
open Fable.TypedJson.Json

#if PYTHON
open Fable.TypedJson.Python.Json

let backend = python
#else
#if JS
open Fable.TypedJson.JS.Json

let backend = js
#else
open Fable.TypedJson.Beam.Json

let backend = beam
#endif
#endif

// ============================================================================
// Test Record Types
// ============================================================================

type SimpleRecord = { Name: string; Age: int }

type RecordWithFloat = {
    AirTemperature: float
    RelativeHumidity: float
}

type RecordWithOption = { Name: string; Email: string option }

type RecordWithBool = { Active: bool; Count: int }

// ============================================================================
// Decode Tests
// ============================================================================

[<Fact>]
let ``test auto decode simple record`` () =
    let codec =
        auto<SimpleRecord> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"name":"Alice","age":30}"""

    match codec.decode map with
    | Ok record ->
        record.Name |> equal "Alice"
        record.Age |> equal 30
    | Error errors -> equal "Ok" (sprintf "Error: %A" errors)

[<Fact>]
let ``test auto decode float record`` () =
    let codec =
        auto<RecordWithFloat> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"air_temperature":22.5,"relative_humidity":65.0}"""

    match codec.decode map with
    | Ok record ->
        record.AirTemperature |> equal 22.5
        record.RelativeHumidity |> equal 65.0
    | Error errors -> equal "Ok" (sprintf "Error: %A" errors)

[<Fact>]
let ``test auto decode with option some`` () =
    let codec =
        auto<RecordWithOption> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"name":"Alice","email":"a@b.com"}"""

    match codec.decode map with
    | Ok record ->
        record.Name |> equal "Alice"
        record.Email |> equal (Some "a@b.com")
    | Error errors -> equal "Ok" (sprintf "Error: %A" errors)

[<Fact>]
let ``test auto decode with option none`` () =
    let codec =
        auto<RecordWithOption> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"name":"Alice"}"""

    match codec.decode map with
    | Ok record ->
        record.Name |> equal "Alice"
        record.Email |> equal None
    | Error errors -> equal "Ok" (sprintf "Error: %A" errors)

[<Fact>]
let ``test auto decode missing required field`` () =
    let codec =
        auto<SimpleRecord> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"name":"Alice"}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 1
        errors.[0].path |> equal "age"

[<Fact>]
let ``test auto decode accumulates all errors`` () =
    let codec =
        auto<SimpleRecord> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors -> errors.Length |> equal 2

// ============================================================================
// Encode Tests
// ============================================================================

[<Fact>]
let ``test auto encode simple record`` () =
    let codec =
        auto<SimpleRecord> ()
        |> withCaseRules CaseRules.SnakeCase

    let record = { Name = "Bob"; Age = 25 }
    let json = codec.encode record
    let map = parseRaw json
    let name = unbox<string> (backend.Get(map, "name"))
    let age = unbox<int> (backend.Get(map, "age"))
    name |> equal "Bob"
    age |> equal 25

[<Fact>]
let ``test auto encode float record`` () =
    let codec =
        auto<RecordWithFloat> ()
        |> withCaseRules CaseRules.SnakeCase

    let record = {
        AirTemperature = 22.5
        RelativeHumidity = 65.0
    }

    let json = codec.encode record
    let map = parseRaw json
    let temp = unbox<float> (backend.Get(map, "air_temperature"))
    let humidity = unbox<float> (backend.Get(map, "relative_humidity"))
    temp |> equal 22.5
    humidity |> equal 65.0

[<Fact>]
let ``test auto encode with option some`` () =
    let codec =
        auto<RecordWithOption> ()
        |> withCaseRules CaseRules.SnakeCase

    let record = {
        Name = "Alice"
        Email = Some "a@b.com"
    }

    let json = codec.encode record
    let map = parseRaw json
    let name = unbox<string> (backend.Get(map, "name"))
    let email = unbox<string> (backend.Get(map, "email"))
    name |> equal "Alice"
    email |> equal "a@b.com"

// ============================================================================
// Round-trip Tests
// ============================================================================

[<Fact>]
let ``test auto round-trip simple record`` () =
    let codec =
        auto<SimpleRecord> ()
        |> withCaseRules CaseRules.SnakeCase

    let original = { Name = "Charlie"; Age = 40 }
    let json = codec.encode original
    let map = parseRaw json

    match codec.decode map with
    | Ok decoded ->
        decoded.Name |> equal original.Name
        decoded.Age |> equal original.Age
    | Error errors -> equal "Ok" (sprintf "Error: %A" errors)

[<Fact>]
let ``test auto round-trip float record`` () =
    let codec =
        auto<RecordWithFloat> ()
        |> withCaseRules CaseRules.SnakeCase

    let original = {
        AirTemperature = -5.3
        RelativeHumidity = 88.2
    }

    let json = codec.encode original
    let map = parseRaw json

    match codec.decode map with
    | Ok decoded ->
        decoded.AirTemperature
        |> equal original.AirTemperature

        decoded.RelativeHumidity
        |> equal original.RelativeHumidity
    | Error errors -> equal "Ok" (sprintf "Error: %A" errors)
