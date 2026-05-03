(**
# TestSchema — Tests for Schema module

Tests for format-agnostic validation: coercion, validateMap (string maps),
validateJson (BEAM maps), JsonValue erased DU, and error handling.
*)

module Fable.TypedJson.Tests.Schema

open Fable.TypedJson.Testing
open Fable.TypedJson.Schema
open Fable.TypedJson.Json

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


// ============================================================================
// Test Record Types
// ============================================================================

type SimpleInput = { Name: string; Age: int }

type FloatInput = { Temperature: float; Humidity: float }

type BoolInput = { Active: bool; Debug: bool }

type OptionalInput = { Name: string; Email: string option }

type MixedInput = {
    Label: string
    Count: int
    Score: float
    Enabled: bool
}

// ============================================================================
// validateMap — String Map (ToolCall.input simulation)
// ============================================================================

[<Fact>]
let ``test validateMap simple strings`` () =
    let input = Map.ofList [ "name", "Alice"; "age", "30" ]

    match validateMap<SimpleInput> input with
    | Ok r ->
        r.Name |> equal "Alice"
        r.Age |> equal 30
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateMap float from string`` () =
    let input = Map.ofList [ "temperature", "22.5"; "humidity", "65.0" ]

    match validateMap<FloatInput> input with
    | Ok r ->
        r.Temperature |> equal 22.5
        r.Humidity |> equal 65.0
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateMap bool from string`` () =
    let input = Map.ofList [ "active", "true"; "debug", "false" ]

    match validateMap<BoolInput> input with
    | Ok r ->
        r.Active |> equal true
        r.Debug |> equal false
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateMap missing required field`` () =
    let input = Map.ofList [ "name", "Alice" ]

    match validateMap<SimpleInput> input with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 1
        errors.[0].path |> equal "age"

[<Fact>]
let ``test validateMap all fields missing`` () =
    let input: Map<string, string> = Map.empty

    match validateMap<SimpleInput> input with
    | Ok _ -> equal "Error" "Ok"
    | Error errors -> errors.Length |> equal 2

[<Fact>]
let ``test validateMap optional present`` () =
    let input = Map.ofList [ "name", "Alice"; "email", "a@b.com" ]

    match validateMap<OptionalInput> input with
    | Ok r ->
        r.Name |> equal "Alice"
        r.Email |> equal (Some "a@b.com")
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateMap optional missing`` () =
    let input = Map.ofList [ "name", "Alice" ]

    match validateMap<OptionalInput> input with
    | Ok r ->
        r.Name |> equal "Alice"
        r.Email |> equal None
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateMap invalid int string`` () =
    let input = Map.ofList [ "name", "Alice"; "age", "not_a_number" ]

    match validateMap<SimpleInput> input with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 1
        errors.[0].path |> equal "age"

        (errors.[0].message.Contains("cannot parse"))
        |> equal true

[<Fact>]
let ``test validateMap invalid float string`` () =
    let input = Map.ofList [ "temperature", "hot"; "humidity", "65.0" ]

    match validateMap<FloatInput> input with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 1
        errors.[0].path |> equal "temperature"

[<Fact>]
let ``test validateMap invalid bool string`` () =
    let input = Map.ofList [ "active", "yes"; "debug", "false" ]

    match validateMap<BoolInput> input with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 1
        errors.[0].path |> equal "active"

[<Fact>]
let ``test validateMap mixed types all from strings`` () =
    let input =
        Map.ofList [ "label", "test"; "count", "5"; "score", "9.5"; "enabled", "true" ]

    match validateMap<MixedInput> input with
    | Ok r ->
        r.Label |> equal "test"
        r.Count |> equal 5
        r.Score |> equal 9.5
        r.Enabled |> equal true
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateMap accumulates multiple errors`` () =
    let input = Map.ofList [ "label", "test" ]

    match validateMap<MixedInput> input with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        // count, score, enabled are all missing
        errors.Length |> equal 3

// ============================================================================
// validateJson — BeamMap (parsed JSON)
// ============================================================================

[<Fact>]
let ``test validateJson with native types`` () =
    let map = parseRaw """{"name":"Alice","age":30}"""

    match validateJson<SimpleInput> map with
    | Ok r ->
        r.Name |> equal "Alice"
        r.Age |> equal 30
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateJson with floats`` () =
    let map = parseRaw """{"temperature":22.5,"humidity":65.0}"""

    match validateJson<FloatInput> map with
    | Ok r ->
        r.Temperature |> equal 22.5
        r.Humidity |> equal 65.0
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateJson with bools`` () =
    let map = parseRaw """{"active":true,"debug":false}"""

    match validateJson<BoolInput> map with
    | Ok r ->
        r.Active |> equal true
        r.Debug |> equal false
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateJson missing field`` () =
    let map = parseRaw """{"name":"Alice"}"""

    match validateJson<SimpleInput> map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 1
        errors.[0].path |> equal "age"

[<Fact>]
let ``test validateJson optional present`` () =
    let map = parseRaw """{"name":"Alice","email":"a@b.com"}"""

    match validateJson<OptionalInput> map with
    | Ok r ->
        r.Name |> equal "Alice"
        r.Email |> equal (Some "a@b.com")
    | Error e -> equal "Ok" (formatErrors e)

[<Fact>]
let ``test validateJson optional missing`` () =
    let map = parseRaw """{"name":"Alice"}"""

    match validateJson<OptionalInput> map with
    | Ok r ->
        r.Name |> equal "Alice"
        r.Email |> equal None
    | Error e -> equal "Ok" (formatErrors e)

// ============================================================================
// Coercion Tests
// ============================================================================

[<Fact>]
let ``test coercion string to int`` () =
    match coerce backend emptyRegistry identityTransform typeof<int> (JString "42") with
    | Ok v -> (unbox<int> v) |> equal 42
    | Error e -> equal "Ok" e

[<Fact>]
let ``test coercion string to float`` () =
    match coerce backend emptyRegistry identityTransform typeof<float> (JString "3.14") with
    | Ok v -> (unbox<float> v) |> equal 3.14
    | Error e -> equal "Ok" e

[<Fact>]
let ``test coercion string to bool true`` () =
    match coerce backend emptyRegistry identityTransform typeof<bool> (JString "true") with
    | Ok v -> (unbox<bool> v) |> equal true
    | Error e -> equal "Ok" e

[<Fact>]
let ``test coercion string to bool false`` () =
    match coerce backend emptyRegistry identityTransform typeof<bool> (JString "false") with
    | Ok v -> (unbox<bool> v) |> equal false
    | Error e -> equal "Ok" e

[<Fact>]
let ``test coercion int to float`` () =
    match coerce backend emptyRegistry identityTransform typeof<float> (JInt 42) with
    | Ok v -> (unbox<float> v) |> equal 42.0
    | Error e -> equal "Ok" e

[<Fact>]
let ``test coercion float to int`` () =
    match coerce backend emptyRegistry identityTransform typeof<int> (JFloat 42.0) with
    | Ok v -> (unbox<int> v) |> equal 42
    | Error e -> equal "Ok" e

[<Fact>]
let ``test coercion int to string`` () =
    match coerce backend emptyRegistry identityTransform typeof<string> (JInt 42) with
    | Ok v -> (unbox<string> v) |> equal "42"
    | Error e -> equal "Ok" e

[<Fact>]
let ``test coercion invalid string to int`` () =
    match coerce backend emptyRegistry identityTransform typeof<int> (JString "not_a_number") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> (msg.Contains("cannot parse")) |> equal true

[<Fact>]
let ``test coercion invalid string to float`` () =
    match coerce backend emptyRegistry identityTransform typeof<float> (JString "not_a_number") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> (msg.Contains("cannot parse")) |> equal true

[<Fact>]
let ``test coercion invalid string to bool`` () =
    match coerce backend emptyRegistry identityTransform typeof<bool> (JString "maybe") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> (msg.Contains("cannot parse")) |> equal true

// ============================================================================
// formatErrors
// ============================================================================

[<Fact>]
let ``test formatErrors produces readable string`` () =
    let errors = [
        {
            path = "name"
            message = "missing field (expected String)"
        }
        {
            path = "age"
            message = "cannot parse 'xyz' as int"
        }
    ]

    let result = formatErrors errors
    (result.Contains("name:")) |> equal true
    (result.Contains("age:")) |> equal true

// ============================================================================
// dump — Record to BeamMap
// ============================================================================

[<Fact>]
let ``test dump simple record`` () =
    let record = { Name = "Alice"; Age = 30 }
    let map = dump record
    let name = getString backend map "name"
    let age = getInt backend map "age"
    name |> equal "Alice"
    age |> equal 30

[<Fact>]
let ``test dump record with option some`` () =
    let record = {
        Name = "Alice"
        Email = Some "a@b.com"
    }

    let map = dump record
    let name = getString backend map "name"
    let email = getString backend map "email"
    name |> equal "Alice"
    email |> equal "a@b.com"

[<Fact>]
let ``test dump record with option none omits field`` () =
    let record = { Name = "Alice"; Email = None }
    let map = dump record
    let hasEmail = backend.ContainsKey(map, "email")
    hasEmail |> equal false
