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

[<Fact>]
let ``test nested record decodes valid input`` () =
    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase

    let map =
        parseRaw """{"name":"Alice","address":{"street":"Main 1","city":"Oslo"}}"""

    match codec.decode map with
    | Ok r ->
        r.Name |> equal "Alice"
        r.Address.Street |> equal "Main 1"
        r.Address.City |> equal "Oslo"
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test nested record reports inner missing field`` () =
    let codec = auto<User> () |> withCaseRules CaseRules.SnakeCase
    let map = parseRaw """{"name":"Alice","address":{"street":"Main 1"}}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs
        // Outer field is "address"; inner failure is "city: ..."
        formatted.Contains "address" |> equal true
        formatted.Contains "city" |> equal true

// ============================================================================
// List of primitives
// ============================================================================

type Tagged = { Title: string; Tags: string list }

[<Fact>]
let ``test list of strings decodes`` () =
    let codec =
        auto<Tagged> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"title":"hello","tags":["a","b","c"]}"""

    match codec.decode map with
    | Ok r ->
        r.Title |> equal "hello"
        r.Tags |> equal [ "a"; "b"; "c" ]
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test list of strings empty decodes`` () =
    let codec =
        auto<Tagged> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"title":"empty","tags":[]}"""

    match codec.decode map with
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
    let codec = auto<Team> () |> withCaseRules CaseRules.SnakeCase

    let map =
        parseRaw
            """{"name":"Engineering","members":[
                {"name":"Alice","address":{"street":"S1","city":"Oslo"}},
                {"name":"Bob","address":{"street":"S2","city":"Bergen"}}
            ]}"""

    match codec.decode map with
    | Ok r ->
        r.Name |> equal "Engineering"
        r.Members.Length |> equal 2
        r.Members.[0].Name |> equal "Alice"
        r.Members.[1].Address.City |> equal "Bergen"
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test list of records reports element index in error`` () =
    let codec = auto<Team> () |> withCaseRules CaseRules.SnakeCase

    let map =
        parseRaw
            """{"name":"Engineering","members":[
                {"name":"Alice","address":{"street":"S1","city":"Oslo"}},
                {"name":"Bob"}
            ]}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs
        formatted.Contains("members") |> equal true
        formatted.Contains("[1]") |> equal true
        formatted.Contains("address") |> equal true

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

[<Fact>]
let ``test encode applies case rule to nested record keys`` () =
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
    backend.ContainsKey(parsed, "customer_name")
    |> equal true

    backend.ContainsKey(parsed, "shipping_address")
    |> equal true

    // Inner keys must follow the same rule.
    let inner = backend.Get(parsed, "shipping_address")

    backend.ContainsKey(inner, "postal_code")
    |> equal true

    backend.ContainsKey(inner, "country_name")
    |> equal true

type Route = { Vendor: string; Stops: Postal list }

[<Fact>]
let ``test encode applies case rule to record-list element keys`` () =
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
    backend.IsArray stops |> equal true
    backend.ArrayLength stops |> equal 1
    let first = backend.ArrayAt(stops, 0)

    backend.ContainsKey(first, "postal_code")
    |> equal true

    backend.ContainsKey(first, "country_name")
    |> equal true

[<Fact>]
let ``test encode round-trips nested record under snake_case`` () =
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
        r.CustomerName |> equal "Alice"
        r.ShippingAddress.PostalCode |> equal "0150"
        r.ShippingAddress.CountryName |> equal "Norway"
    | Error errs -> equal "Ok" (formatErrors errs)
