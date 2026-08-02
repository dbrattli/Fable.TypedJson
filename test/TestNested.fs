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

// ============================================================================
// Recursive types round-trip
// ============================================================================

(**
`TestJsonSchema` covers recursive types for the *schema emitter* only, where the
guard is a `visited` path. Decode and encode have no such guard — they terminate
because `coerce` recurses lazily at runtime and runs out of document.

These tests pin that behaviour so it survives a move to a codec that resolves
reflection at construction time. An eagerly built plan tree has no natural base
case and would hang at `auto<Tree> ()` rather than at decode, so this is the
regression net for that change.

invariant: a self-referential record round-trips to its full depth — the guard is the data, not the type
*)
type Tree = { Label: string; Children: Tree list }

type Node = { Name: string; Next: Node option }

type Branch = { Tag: string; Leaf: Twig option }

and Twig = { Kind: string; Parent: Branch option }

let private recursiveTypeTests =
    testList (
        "Recursive types round-trip",
        [
            test (
                "self-referential record decodes nested children",
                fun _ ->
                    let codec = auto<Tree> ()

                    let map =
                        parseRaw
                            """{"label":"root","children":[{"label":"a","children":[]},{"label":"b","children":[{"label":"b1","children":[]}]}]}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Label (isEqualTo "root")
                        assertThat r.Children.Length (isEqualTo 2)
                        assertThat r.Children.[0].Label (isEqualTo "a")
                        assertThat r.Children.[1].Label (isEqualTo "b")
                        assertThat r.Children.[1].Children.[0].Label (isEqualTo "b1")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "self-referential record round-trips through encode",
                fun _ ->
                    let codec = auto<Tree> ()

                    let original = {
                        Label = "root"
                        Children = [
                            {
                                Label = "a"
                                Children = [ { Label = "a1"; Children = [] } ]
                            }
                        ]
                    }

                    match codec.decode (parseRaw (codec.encode original)) with
                    | Ok r ->
                        assertThat r.Label (isEqualTo "root")
                        assertThat r.Children.[0].Label (isEqualTo "a")
                        assertThat r.Children.[0].Children.[0].Label (isEqualTo "a1")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "option-recursive record decodes a chain",
                fun _ ->
                    let codec = auto<Node> ()
                    let map = parseRaw """{"name":"a","next":{"name":"b","next":{"name":"c"}}}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "a")

                        match r.Next with
                        | Some second ->
                            assertThat second.Name (isEqualTo "b")

                            match second.Next with
                            | Some third -> assertThat third.Name (isEqualTo "c")
                            | Option.None -> assertThat "missing third" (isEqualTo "c")
                        | Option.None -> assertThat "missing second" (isEqualTo "b")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "option-recursive record terminates on a missing tail",
                fun _ ->
                    let codec = auto<Node> ()

                    match codec.decode (parseRaw """{"name":"only"}""") with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "only")
                        assertThat r.Next.IsNone isTrue
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "mutually recursive records round-trip",
                fun _ ->
                    let codec = auto<Branch> ()
                    let map = parseRaw """{"tag":"b","leaf":{"kind":"k"}}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Tag (isEqualTo "b")

                        match r.Leaf with
                        | Some leaf ->
                            assertThat leaf.Kind (isEqualTo "k")
                            assertThat leaf.Parent.IsNone isTrue
                        | Option.None -> assertThat "missing leaf" (isEqualTo "k")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// Array-typed fields
// ============================================================================

(**
`'T[]` record fields had no coverage at all. They take a different path from
`'T list`: decode goes through `coerceArray` (which builds an `obj[]`, with no
typed-array counterpart to `buildList`) and encode through `transformValue`'s
`unbox<obj[]>`. Both are suspect on the CLR, where array covariance covers
reference types only — so `int[]` and `float[]` are the cases that matter, not
`string[]`.

invariant: a `'T[]` field behaves the same as the equivalent `'T list` field on every target
*)
type Scores = { Player: string; Points: int[] }

type Labels = { Owner: string; Names: string[] }

let private arrayFieldTests =
    testList (
        "Array-typed fields",
        [
            test (
                "decodes a value-type array field",
                fun _ ->
                    let codec = auto<Scores> ()

                    match codec.decode (parseRaw """{"player":"Alice","points":[1,2,3]}""") with
                    | Ok r ->
                        assertThat r.Player (isEqualTo "Alice")
                        assertThat r.Points.Length (isEqualTo 3)
                        assertThat r.Points.[0] (isEqualTo 1)
                        assertThat r.Points.[2] (isEqualTo 3)
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "encodes a value-type array field as a JSON array",
                fun _ ->
                    let codec = auto<Scores> ()

                    let parsed =
                        parseRaw (
                            codec.encode {
                                Player = "Alice"
                                Points = [| 1; 2; 3 |]
                            }
                        )

                    let points = backend.Get(parsed, "points")
                    assertThat (backend.IsArray points) isTrue
                    assertThat (backend.ArrayLength points) (isEqualTo 3)
            )
            test (
                "round-trips a reference-type array field",
                fun _ ->
                    let codec = auto<Labels> ()

                    let original = {
                        Owner = "Acme"
                        Names = [| "a"; "b" |]
                    }

                    match codec.decode (parseRaw (codec.encode original)) with
                    | Ok r ->
                        assertThat r.Owner (isEqualTo "Acme")
                        assertThat r.Names.Length (isEqualTo 2)
                        assertThat r.Names.[1] (isEqualTo "b")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "decodes an empty array field",
                fun _ ->
                    let codec = auto<Scores> ()

                    match codec.decode (parseRaw """{"player":"Bob","points":[]}""") with
                    | Ok r -> assertThat r.Points.Length (isEqualTo 0)
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
            recursiveTypeTests
            arrayFieldTests
        ]
    )
