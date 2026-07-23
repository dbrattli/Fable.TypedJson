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

// ============================================================================
// Nested record
// ============================================================================

type Address = { Street: string; City: string }

type User = { Name: string; Address: Address }

let private nestedRecordTests =
    testList (
        "Nested record",
        [
            test (
                "nested record decodes valid input",
                fun _ ->
                    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase

                    let map =
                        parseRaw """{"name":"Alice","address":{"street":"Main 1","city":"Oslo"}}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "Alice")
                        assertThat r.Address.Street (isEqualTo "Main 1")
                        assertThat r.Address.City (isEqualTo "Oslo")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "nested record reports inner missing field",
                fun _ ->
                    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
                    let map = parseRaw """{"name":"Alice","address":{"street":"Main 1"}}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs
                        // Outer field is "address"; inner failure is "city: ..."
                        assertThat (formatted.Contains "address") isTrue
                        assertThat (formatted.Contains "city") isTrue
            )
        ]
    )

// ============================================================================
// List of primitives
// ============================================================================

type Tagged = { Title: string; Tags: string list }

let private listOfPrimitivesTests =
    testList (
        "List of primitives",
        [
            test (
                "list of strings decodes",
                fun _ ->
                    let codec =
                        auto<Tagged> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"title":"hello","tags":["a","b","c"]}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Title (isEqualTo "hello")
                        assertThat r.Tags (isEqualTo [ "a"; "b"; "c" ])
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "list of strings empty decodes",
                fun _ ->
                    let codec =
                        auto<Tagged> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"title":"empty","tags":[]}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Tags (isEqualTo [])
                        assertThat r.Title (isEqualTo "empty")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// List of records
// ============================================================================

type Team = { Name: string; Members: User list }

let private listOfRecordsTests =
    testList (
        "List of records",
        [
            test (
                "list of records decodes",
                fun _ ->
                    let codec = auto<Team> () |> withCaseRules CaseRules.SnakeCase

                    let map =
                        parseRaw
                            """{"name":"Engineering","members":[
                                {"name":"Alice","address":{"street":"S1","city":"Oslo"}},
                                {"name":"Bob","address":{"street":"S2","city":"Bergen"}}
                            ]}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "Engineering")
                        assertThat r.Members.Length (isEqualTo 2)
                        assertThat (r.Members.[0].Name) (isEqualTo "Alice")
                        assertThat (r.Members.[1].Address.City) (isEqualTo "Bergen")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "list of records reports element index in error",
                fun _ ->
                    let codec = auto<Team> () |> withCaseRules CaseRules.SnakeCase

                    let map =
                        parseRaw
                            """{"name":"Engineering","members":[
                                {"name":"Alice","address":{"street":"S1","city":"Oslo"}},
                                {"name":"Bob"}
                            ]}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs
                        assertThat (formatted.Contains("members")) isTrue
                        assertThat (formatted.Contains("[1]")) isTrue
                        assertThat (formatted.Contains("address")) isTrue
            )
        ]
    )

// ============================================================================
// CaseRules apply recursively on encode
// Multi-word field names so the rule is visible (snake_case → snake_case is
// only a no-op when names are single words).
// ============================================================================

type Postal = {
    PostalCode: string
    CountryName: string
}

type Customer = {
    CustomerName: string
    ShippingAddress: Postal
}

type Route = { Vendor: string; Stops: Postal list }

let private caseRulesRecursiveTests =
    testList (
        "CaseRules apply recursively on encode",
        [
            test (
                "encode applies case rule to nested record keys",
                fun _ ->
                    let codec =
                        auto<Customer> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let json =
                        codec.encode {
                            CustomerName = "Alice"
                            ShippingAddress = {
                                PostalCode = "0150"
                                CountryName = "Norway"
                            }
                        }

                    let parsed = parseRaw json
                    // Outer keys
                    assertThat (backend.ContainsKey(parsed, "customer_name")) isTrue

                    assertThat (backend.ContainsKey(parsed, "shipping_address")) isTrue

                    // Inner keys must follow the same rule.
                    let inner = backend.Get(parsed, "shipping_address")

                    assertThat (backend.ContainsKey(inner, "postal_code")) isTrue

                    assertThat (backend.ContainsKey(inner, "country_name")) isTrue
            )
            test (
                "encode applies case rule to record-list element keys",
                fun _ ->
                    let codec =
                        auto<Route> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let json =
                        codec.encode {
                            Vendor = "Acme"
                            Stops = [
                                {
                                    PostalCode = "0150"
                                    CountryName = "Norway"
                                }
                            ]
                        }

                    let parsed = parseRaw json
                    let stops = backend.Get(parsed, "stops")
                    assertThat (backend.IsArray stops) isTrue
                    assertThat (backend.ArrayLength stops) (isEqualTo 1)
                    let first = backend.ArrayAt(stops, 0)

                    assertThat (backend.ContainsKey(first, "postal_code")) isTrue

                    assertThat (backend.ContainsKey(first, "country_name")) isTrue
            )
            test (
                "encode round-trips nested record under snake_case",
                fun _ ->
                    let codec =
                        auto<Customer> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let original = {
                        CustomerName = "Alice"
                        ShippingAddress = {
                            PostalCode = "0150"
                            CountryName = "Norway"
                        }
                    }

                    let json = codec.encode original
                    let map = parseRaw json

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.CustomerName (isEqualTo "Alice")
                        assertThat r.ShippingAddress.PostalCode (isEqualTo "0150")
                        assertThat r.ShippingAddress.CountryName (isEqualTo "Norway")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
        ]
    )

let tests =
    testList (
        "Nested",
        [
            nestedRecordTests
            listOfPrimitivesTests
            listOfRecordsTests
            caseRulesRecursiveTests
        ]
    )
