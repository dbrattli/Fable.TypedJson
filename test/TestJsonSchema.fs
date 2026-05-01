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
open Fable.TypedJson.Beam.Json
let backend = beam
#endif

// ============================================================================
// Primitives + required fields
// ============================================================================

type Simple = { Name: string; Age: int }

[<Fact>]
let ``test schema of simple record`` () =
    let json = jsonSchemaOf<Simple> emptyRegistry CaseRules.SnakeCase
    let parsed = parseRaw json

    unbox<string> (backend.Get(parsed, "type")) |> equal "object"
    unbox<string> (backend.Get(parsed, "title")) |> equal "Simple"

    let props = backend.Get(parsed, "properties")
    backend.IsMap props |> equal true

    let nameSchema = backend.Get(props, "name")
    unbox<string> (backend.Get(nameSchema, "type")) |> equal "string"

    let ageSchema = backend.Get(props, "age")
    unbox<string> (backend.Get(ageSchema, "type")) |> equal "integer"

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
    unbox<string> (backend.ArrayAt(required, 0)) |> equal "name"

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

    unbox<string> (backend.Get(addressSchema, "type")) |> equal "object"
    unbox<string> (backend.Get(addressSchema, "title")) |> equal "Address"

    let innerProps = backend.Get(addressSchema, "properties")
    let cityField = backend.Get(innerProps, "city")
    unbox<string> (backend.Get(cityField, "type")) |> equal "string"

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

    unbox<string> (backend.Get(tagsSchema, "type")) |> equal "array"
    let items = backend.Get(tagsSchema, "items")
    unbox<string> (backend.Get(items, "type")) |> equal "string"

// ============================================================================
// CaseRules applied to property keys
// ============================================================================

type Weather = { AirTemperature: float; WindSpeed: float }

[<Fact>]
let ``test schema property names follow case rule`` () =
    let snakeJson = jsonSchemaOf<Weather> emptyRegistry CaseRules.SnakeCase
    let snakeParsed = parseRaw snakeJson
    let snakeProps = backend.Get(snakeParsed, "properties")
    backend.ContainsKey(snakeProps, "air_temperature") |> equal true
    backend.ContainsKey(snakeProps, "wind_speed") |> equal true

    let camelJson = jsonSchemaOf<Weather> emptyRegistry CaseRules.LowerFirst
    let camelParsed = parseRaw camelJson
    let camelProps = backend.Get(camelParsed, "properties")
    backend.ContainsKey(camelProps, "airTemperature") |> equal true
    backend.ContainsKey(camelProps, "windSpeed") |> equal true

// ============================================================================
// Refined types pull their schema (with constraints) through the registry
// ============================================================================

open Fable.TypedJson.Refined

type Account = { Username: NonEmptyString; Email: Email }

[<Fact>]
let ``test schema of refined type fields includes constraints`` () =
    let codecs = emptyRegistry |> registerAll
    let json = jsonSchemaOf<Account> codecs CaseRules.SnakeCase
    let parsed = parseRaw json

    let props = backend.Get(parsed, "properties")

    // NonEmptyString carries `minLength: 1` (from `Codec.nonEmpty`).
    let usernameSchema = backend.Get(props, "username")
    unbox<string> (backend.Get(usernameSchema, "type")) |> equal "string"
    unbox<int> (backend.Get(usernameSchema, "minLength")) |> equal 1

    // Email carries `pattern` (from `Codec.pattern`).
    let emailSchema = backend.Get(props, "email")
    unbox<string> (backend.Get(emailSchema, "type")) |> equal "string"
    backend.ContainsKey(emailSchema, "pattern") |> equal true
