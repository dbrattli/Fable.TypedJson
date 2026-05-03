(**
# TestJsonSchema — Phase 3.11: JSON Schema generation

Verifies that `jsonSchemaOf<'T>` walks the F# record's reflection plus the
codec registry to emit a valid JSON Schema document. Covers primitives,
optional fields, nested records, lists, and round-trip via the backend's
parser so we test against the actual output structure rather than fragile
string comparisons.
*)

module Fable.TypedJson.Tests.JsonSchema

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
// Primitives + required fields
// ============================================================================

type Simple = { Name: string; Age: int }

[<Fact>]
let ``test schema of simple record`` () =
    let json = jsonSchemaOf<Simple> emptyRegistry CaseRules.SnakeCase
    let parsed = parseRaw json

    getString backend parsed "type" |> equal "object"
    getString backend parsed "title" |> equal "Simple"

    let props = backend.Get(parsed, "properties")
    backend.IsMap props |> equal true

    let nameSchema = backend.Get(props, "name")

    getString backend nameSchema "type"
    |> equal "string"

    let ageSchema = backend.Get(props, "age")

    getString backend ageSchema "type"
    |> equal "integer"

    // Required: both fields, since neither is optional.
    let required = backend.Get(parsed, "required")
    backend.IsArray required |> equal true
    backend.ArrayLength required |> equal 2

type WithOptional = { Name: string; Email: string option }

[<Fact>]
let ``test schema omits optional field from required`` () =
    let json = jsonSchemaOf<WithOptional> emptyRegistry CaseRules.SnakeCase
    let parsed = parseRaw json

    let required = backend.Get(parsed, "required")
    backend.ArrayLength required |> equal 1

    arrayAtString backend required 0 |> equal "name"

    // Email field still present in properties, just not required.
    let props = backend.Get(parsed, "properties")
    backend.ContainsKey(props, "email") |> equal true

// ============================================================================
// Nested records
// ============================================================================

type Address = { Street: string; City: string }

type User = { Name: string; Address: Address }

[<Fact>]
let ``test schema of nested record`` () =
    let json = jsonSchemaOf<User> emptyRegistry CaseRules.SnakeCase
    let parsed = parseRaw json

    let props = backend.Get(parsed, "properties")
    let addressSchema = backend.Get(props, "address")

    getString backend addressSchema "type"
    |> equal "object"

    getString backend addressSchema "title"
    |> equal "Address"

    let innerProps = backend.Get(addressSchema, "properties")
    let cityField = backend.Get(innerProps, "city")

    getString backend cityField "type"
    |> equal "string"

// ============================================================================
// Lists
// ============================================================================

type Tagged = { Title: string; Tags: string list }

[<Fact>]
let ``test schema of list field`` () =
    let json = jsonSchemaOf<Tagged> emptyRegistry CaseRules.SnakeCase
    let parsed = parseRaw json

    let props = backend.Get(parsed, "properties")
    let tagsSchema = backend.Get(props, "tags")

    getString backend tagsSchema "type"
    |> equal "array"

    let items = backend.Get(tagsSchema, "items")
    getString backend items "type" |> equal "string"

// ============================================================================
// CaseRules applied to property keys
// ============================================================================

type Weather = {
    AirTemperature: float
    WindSpeed: float
}

[<Fact>]
let ``test schema property names follow case rule`` () =
    let snakeJson = jsonSchemaOf<Weather> emptyRegistry CaseRules.SnakeCase
    let snakeParsed = parseRaw snakeJson
    let snakeProps = backend.Get(snakeParsed, "properties")

    backend.ContainsKey(snakeProps, "air_temperature")
    |> equal true

    backend.ContainsKey(snakeProps, "wind_speed")
    |> equal true

    let camelJson = jsonSchemaOf<Weather> emptyRegistry CaseRules.LowerFirst
    let camelParsed = parseRaw camelJson
    let camelProps = backend.Get(camelParsed, "properties")

    backend.ContainsKey(camelProps, "airTemperature")
    |> equal true

    backend.ContainsKey(camelProps, "windSpeed")
    |> equal true

// ============================================================================
// Refined types pull their schema (with constraints) through the registry
// ============================================================================

open Fable.TypedJson.Refined

type Account = {
    Username: NonEmptyString
    Email: Email
}

[<Fact>]
let ``test schema of refined type fields includes constraints`` () =
    let codecs = emptyRegistry |> registerAll
    let json = jsonSchemaOf<Account> codecs CaseRules.SnakeCase
    let parsed = parseRaw json

    let props = backend.Get(parsed, "properties")

    // NonEmptyString carries `minLength: 1` (from `Codec.nonEmpty`).
    let usernameSchema = backend.Get(props, "username")

    getString backend usernameSchema "type"
    |> equal "string"

    getInt backend usernameSchema "minLength"
    |> equal 1

    // Email carries `pattern` (from `Codec.pattern`).
    let emailSchema = backend.Get(props, "email")

    getString backend emailSchema "type"
    |> equal "string"

    backend.ContainsKey(emailSchema, "pattern")
    |> equal true
