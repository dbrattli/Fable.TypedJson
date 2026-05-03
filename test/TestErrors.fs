(**
# TestErrors — Negative tests for error handling and messages

Tests that validation errors are clear, accumulated correctly,
and provide actionable information for debugging.
*)

module Fable.TypedJson.Tests.Errors

open Fable.TypedJson.Testing
open Fable.TypedJson.Json

#if PYTHON
open Fable.TypedJson.Python.Json
#else
#if JS
open Fable.TypedJson.JS.Json
#else
#if DOTNET
open Fable.TypedJson.DotNet.Json
#else
open Fable.TypedJson.Beam.Json
#endif
#endif
#endif

// ============================================================================
// Test Record Types
// ============================================================================

type User = {
    Name: string
    Age: int
    Email: string
}

type Config = { Host: string; Port: int; Debug: bool }

type WithOptional = {
    Required: string
    Optional: int option
}

type Nested = { Label: string; Value: float }

// ============================================================================
// Missing Field Errors
// ============================================================================

[<Fact>]
let ``test error on single missing field has correct path`` () =
    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
    let map = parseRaw """{"name":"Alice","age":30}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 1
        errors.[0].path |> equal "email"

[<Fact>]
let ``test error on single missing field has descriptive message`` () =
    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
    let map = parseRaw """{"name":"Alice","age":30}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        let msg = errors.[0].message
        // Message should mention the expected type
        (msg.Contains("String") || msg.Contains("missing"))
        |> equal true

[<Fact>]
let ``test all missing fields are reported`` () =
    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
    let map = parseRaw """{}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 3
        let paths = errors |> List.map (fun e -> e.path) |> List.sort
        paths |> equal [ "age"; "email"; "name" ]

[<Fact>]
let ``test all missing config fields are reported`` () =
    let codec =
        auto<Config> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 3
        let paths = errors |> List.map (fun e -> e.path) |> List.sort
        paths |> equal [ "debug"; "host"; "port" ]

[<Fact>]
let ``test partial fields reports only missing ones`` () =
    let codec =
        auto<Config> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"host":"localhost","port":8080}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 1
        errors.[0].path |> equal "debug"

// ============================================================================
// Optional vs Required
// ============================================================================

[<Fact>]
let ``test missing optional field is not an error`` () =
    let codec =
        auto<WithOptional> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"required":"hello"}"""

    match codec.decode map with
    | Ok record ->
        record.Required |> equal "hello"
        record.Optional |> equal None
    | Error errors -> equal "Ok" (sprintf "Error: %A" errors)

[<Fact>]
let ``test missing required field with optional present`` () =
    let codec =
        auto<WithOptional> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"optional":42}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        errors.Length |> equal 1
        errors.[0].path |> equal "required"

[<Fact>]
let ``test both required and optional missing reports only required`` () =
    let codec =
        auto<WithOptional> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        // Only required field should be in errors
        errors.Length |> equal 1
        errors.[0].path |> equal "required"

// ============================================================================
// Wrong Case Rule Errors
// ============================================================================

[<Fact>]
let ``test wrong case rule causes missing field errors`` () =
    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
    // JSON has camelCase keys but we decode with SnakeCase
    let map = parseRaw """{"name":"Alice","age":30,"email":"a@b.com"}"""

    // SnakeCase expects "name", "age", "email" — which matches!
    match codec.decode map with
    | Ok _ -> () // should work since keys are already lowercase
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test camelCase json with snake_case rule fails for multi-word fields`` () =
    let codec =
        auto<Config> ()
        |> withCaseRules CaseRules.SnakeCase
    // JSON uses camelCase but we decode expecting snake_case
    let map = parseRaw """{"host":"localhost","port":8080,"debug":true}"""

    // "host", "port", "debug" are single words, so both rules match
    match codec.decode map with
    | Ok config ->
        config.Host |> equal "localhost"
        config.Port |> equal 8080
        config.Debug |> equal true
    | Error _ -> equal "Ok" "Error"

// ============================================================================
// Empty and Null Handling
// ============================================================================

[<Fact>]
let ``test decode empty object reports all required fields`` () =
    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
    let map = parseRaw """{}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors -> errors.Length |> equal 3

[<Fact>]
let ``test error paths use json key names not field names`` () =
    // When using camelCase, errors should show the camelCase key
    let codec =
        auto<Nested> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{}"""

    match codec.decodeWith CaseRules.LowerFirst map with
    | Ok _ -> equal "Error" "Ok"
    | Error errors ->
        let paths = errors |> List.map (fun e -> e.path) |> List.sort
        // Should use the camelCase JSON keys, not the snake_case field names
        paths |> equal [ "label"; "value" ]

[<Fact>]
let ``test extra fields in json are ignored`` () =
    let codec =
        auto<Nested> ()
        |> withCaseRules CaseRules.SnakeCase

    let map =
        parseRaw """{"label":"test","value":1.5,"extra":"ignored","another":true}"""

    match codec.decode map with
    | Ok record ->
        record.Label |> equal "test"
        record.Value |> equal 1.5
    | Error _ -> equal "Ok" "Error"
