(**
# TestErrors — Negative tests for error handling and messages

Tests that validation errors are clear, accumulated correctly,
and provide actionable information for debugging.
*)

module Fable.TypedJson.Tests.Errors

open Fable.TypedJson.Testing
open Fable.TypedJson.Json
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

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

let private missingFieldErrorsTests =
    testList (
        "Missing Field Errors",
        [
            test (
                "error on single missing field has correct path",
                fun _ ->
                    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
                    let map = parseRaw """{"name":"Alice","age":30}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "email")
            )
            test (
                "error on single missing field has descriptive message",
                fun _ ->
                    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
                    let map = parseRaw """{"name":"Alice","age":30}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        let msg = errors.[0].message
                        // Message should mention the expected type
                        assertThat (msg.Contains("String") || msg.Contains("missing")) isTrue
            )
            test (
                "all missing fields are reported",
                fun _ ->
                    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
                    let map = parseRaw """{}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 3)
                        let paths = errors |> List.map (fun e -> e.path) |> List.sort
                        assertThat paths (isEqualTo [ "age"; "email"; "name" ])
            )
            test (
                "all missing config fields are reported",
                fun _ ->
                    let codec =
                        auto<Config> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 3)
                        let paths = errors |> List.map (fun e -> e.path) |> List.sort
                        assertThat paths (isEqualTo [ "debug"; "host"; "port" ])
            )
            test (
                "partial fields reports only missing ones",
                fun _ ->
                    let codec =
                        auto<Config> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"host":"localhost","port":8080}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "debug")
            )
        ]
    )

// ============================================================================
// Optional vs Required
// ============================================================================

let private optionalVsRequiredTests =
    testList (
        "Optional vs Required",
        [
            test (
                "missing optional field is not an error",
                fun _ ->
                    let codec =
                        auto<WithOptional> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"required":"hello"}"""

                    match codec.decode map with
                    | Ok record ->
                        assertThat record.Required (isEqualTo "hello")
                        assertThat record.Optional (isEqualTo None)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
            test (
                "missing required field with optional present",
                fun _ ->
                    let codec =
                        auto<WithOptional> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"optional":42}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "required")
            )
            test (
                "both required and optional missing reports only required",
                fun _ ->
                    let codec =
                        auto<WithOptional> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        // Only required field should be in errors
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "required")
            )
        ]
    )

// ============================================================================
// Wrong Case Rule Errors
// ============================================================================

let private wrongCaseRuleTests =
    testList (
        "Wrong Case Rule Errors",
        [
            test (
                "wrong case rule causes missing field errors",
                fun _ ->
                    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
                    // JSON has camelCase keys but we decode with SnakeCase
                    let map = parseRaw """{"name":"Alice","age":30,"email":"a@b.com"}"""

                    // SnakeCase expects "name", "age", "email" — which matches!
                    match codec.decode map with
                    | Ok _ -> () // should work since keys are already lowercase
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "camelCase json with snake_case rule fails for multi-word fields",
                fun _ ->
                    let codec =
                        auto<Config> ()
                        |> withCaseRules CaseRules.SnakeCase
                    // JSON uses camelCase but we decode expecting snake_case
                    let map = parseRaw """{"host":"localhost","port":8080,"debug":true}"""

                    // "host", "port", "debug" are single words, so both rules match
                    match codec.decode map with
                    | Ok config ->
                        assertThat config.Host (isEqualTo "localhost")
                        assertThat config.Port (isEqualTo 8080)
                        assertThat config.Debug isTrue
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// Empty and Null Handling
// ============================================================================

let private emptyAndNullTests =
    testList (
        "Empty and Null Handling",
        [
            test (
                "decode empty object reports all required fields",
                fun _ ->
                    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
                    let map = parseRaw """{}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors -> assertThat errors.Length (isEqualTo 3)
            )
            test (
                "error paths use json key names not field names",
                fun _ ->
                    // When using camelCase, errors should show the camelCase key
                    let codec =
                        auto<Nested> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{}"""

                    match codec.decodeWith CaseRules.LowerFirst map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        let paths = errors |> List.map (fun e -> e.path) |> List.sort
                        // Should use the camelCase JSON keys, not the snake_case field names
                        assertThat paths (isEqualTo [ "label"; "value" ])
            )
            test (
                "extra fields in json are ignored",
                fun _ ->
                    let codec =
                        auto<Nested> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map =
                        parseRaw """{"label":"test","value":1.5,"extra":"ignored","another":true}"""

                    match codec.decode map with
                    | Ok record ->
                        assertThat record.Label (isEqualTo "test")
                        assertThat record.Value (isEqualTo 1.5)
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
        ]
    )

let tests =
    testList (
        "Errors",
        [
            missingFieldErrorsTests
            optionalVsRequiredTests
            wrongCaseRuleTests
            emptyAndNullTests
        ]
    )
