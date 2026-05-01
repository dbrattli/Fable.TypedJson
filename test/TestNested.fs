(**
# TestNested — Phase 1.9: nested records and lists

Verifies that `auto<'T>` recurses into record-typed fields, list-of-record
fields, list-of-primitive fields, and combinations thereof, with errors
properly surfaced.
*)

module Fable.TypedJson.Tests.Nested

open Fable.TypedJson.Testing
open Fable.TypedJson.Schema
open Fable.TypedJson.Json

#if PYTHON
open Fable.TypedJson.Python.Json
#else
open Fable.TypedJson.Beam.Json
#endif

// ============================================================================
// Nested record
// ============================================================================

type Address = { Street: string; City: string }

type User = { Name: string; Address: Address }

[<Fact>]
let ``test nested record decodes valid input`` () =
    let codec = auto<User> ()
    let map = parseRaw """{"name":"Alice","address":{"street":"Main 1","city":"Oslo"}}"""

    match codec.decode CaseRules.SnakeCase map with
    | Ok r ->
        r.Name |> equal "Alice"
        r.Address.Street |> equal "Main 1"
        r.Address.City |> equal "Oslo"
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test nested record reports inner missing field`` () =
    let codec = auto<User> ()
    let map = parseRaw """{"name":"Alice","address":{"street":"Main 1"}}"""

    match codec.decode CaseRules.SnakeCase map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs
        // Outer field is "address"; inner failure is "city: ..."
        formatted.Contains("address") |> equal true
        formatted.Contains("city") |> equal true

// ============================================================================
// List of primitives
// ============================================================================

type Tagged = { Title: string; Tags: string list }

[<Fact>]
let ``test list of strings decodes`` () =
    let codec = auto<Tagged> ()
    let map = parseRaw """{"title":"hello","tags":["a","b","c"]}"""

    match codec.decode CaseRules.SnakeCase map with
    | Ok r ->
        r.Title |> equal "hello"
        r.Tags |> equal [ "a"; "b"; "c" ]
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test list of strings empty decodes`` () =
    let codec = auto<Tagged> ()
    let map = parseRaw """{"title":"empty","tags":[]}"""

    match codec.decode CaseRules.SnakeCase map with
    | Ok r ->
        r.Tags |> equal []
        r.Title |> equal "empty"
    | Error errs -> equal "Ok" (formatErrors errs)

// ============================================================================
// List of records
// ============================================================================

type Team = { Name: string; Members: User list }

[<Fact>]
let ``test list of records decodes`` () =
    let codec = auto<Team> ()

    let map =
        parseRaw
            """{"name":"Engineering","members":[
                {"name":"Alice","address":{"street":"S1","city":"Oslo"}},
                {"name":"Bob","address":{"street":"S2","city":"Bergen"}}
            ]}"""

    match codec.decode CaseRules.SnakeCase map with
    | Ok r ->
        r.Name |> equal "Engineering"
        r.Members.Length |> equal 2
        r.Members.[0].Name |> equal "Alice"
        r.Members.[1].Address.City |> equal "Bergen"
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test list of records reports element index in error`` () =
    let codec = auto<Team> ()

    let map =
        parseRaw
            """{"name":"Engineering","members":[
                {"name":"Alice","address":{"street":"S1","city":"Oslo"}},
                {"name":"Bob"}
            ]}"""

    match codec.decode CaseRules.SnakeCase map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs
        formatted.Contains("members") |> equal true
        formatted.Contains("[1]") |> equal true
        formatted.Contains("address") |> equal true
