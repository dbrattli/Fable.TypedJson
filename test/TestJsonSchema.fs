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
// Primitives + required fields
// ============================================================================

type Simple = { Name: string; Age: int }

type WithOptional = { Name: string; Email: string option }

let private primitivesRequiredFieldsTests =
    testList (
        "Primitives + required fields",
        [
            test (
                "schema of simple record",
                fun _ ->
                    let json = jsonSchemaOf<Simple> emptyRegistry CaseRules.SnakeCase
                    let parsed = parseRaw json

                    assertThat (getString backend parsed "type") (isEqualTo "object")
                    assertThat (getString backend parsed "title") (isEqualTo "Simple")

                    let props = backend.Get(parsed, "properties")
                    assertThat (backend.IsMap props) isTrue

                    let nameSchema = backend.Get(props, "name")

                    assertThat (getString backend nameSchema "type") (isEqualTo "string")

                    let ageSchema = backend.Get(props, "age")

                    assertThat (getString backend ageSchema "type") (isEqualTo "integer")

                    // Required: both fields, since neither is optional.
                    let required = backend.Get(parsed, "required")
                    assertThat (backend.IsArray required) isTrue
                    assertThat (backend.ArrayLength required) (isEqualTo 2)
            )
            test (
                "schema omits optional field from required",
                fun _ ->
                    let json = jsonSchemaOf<WithOptional> emptyRegistry CaseRules.SnakeCase
                    let parsed = parseRaw json

                    let required = backend.Get(parsed, "required")
                    assertThat (backend.ArrayLength required) (isEqualTo 1)

                    assertThat (arrayAtString backend required 0) (isEqualTo "name")

                    // Email field still present in properties, just not required.
                    let props = backend.Get(parsed, "properties")
                    assertThat (backend.ContainsKey(props, "email")) isTrue
            )
        ]
    )

// ============================================================================
// Nested records
// ============================================================================

type Address = { Street: string; City: string }

type User = { Name: string; Address: Address }

let private nestedRecordsTests =
    testList (
        "Nested records",
        [
            test (
                "schema of nested record",
                fun _ ->
                    let json = jsonSchemaOf<User> emptyRegistry CaseRules.SnakeCase
                    let parsed = parseRaw json

                    let props = backend.Get(parsed, "properties")
                    let addressSchema = backend.Get(props, "address")

                    assertThat (getString backend addressSchema "type") (isEqualTo "object")

                    assertThat (getString backend addressSchema "title") (isEqualTo "Address")

                    let innerProps = backend.Get(addressSchema, "properties")
                    let cityField = backend.Get(innerProps, "city")

                    assertThat (getString backend cityField "type") (isEqualTo "string")
            )
        ]
    )

// ============================================================================
// Lists
// ============================================================================

type Tagged = { Title: string; Tags: string list }

let private listsTests =
    testList (
        "Lists",
        [
            test (
                "schema of list field",
                fun _ ->
                    let json = jsonSchemaOf<Tagged> emptyRegistry CaseRules.SnakeCase
                    let parsed = parseRaw json

                    let props = backend.Get(parsed, "properties")
                    let tagsSchema = backend.Get(props, "tags")

                    assertThat (getString backend tagsSchema "type") (isEqualTo "array")

                    let items = backend.Get(tagsSchema, "items")
                    assertThat (getString backend items "type") (isEqualTo "string")
            )
        ]
    )

// ============================================================================
// CaseRules applied to property keys
// ============================================================================

type Weather = {
    AirTemperature: float
    WindSpeed: float
}

let private caseRulesPropertyKeysTests =
    testList (
        "CaseRules applied to property keys",
        [
            test (
                "schema property names follow case rule",
                fun _ ->
                    let snakeJson = jsonSchemaOf<Weather> emptyRegistry CaseRules.SnakeCase
                    let snakeParsed = parseRaw snakeJson
                    let snakeProps = backend.Get(snakeParsed, "properties")

                    assertThat (backend.ContainsKey(snakeProps, "air_temperature")) isTrue

                    assertThat (backend.ContainsKey(snakeProps, "wind_speed")) isTrue

                    let camelJson = jsonSchemaOf<Weather> emptyRegistry CaseRules.LowerFirst
                    let camelParsed = parseRaw camelJson
                    let camelProps = backend.Get(camelParsed, "properties")

                    assertThat (backend.ContainsKey(camelProps, "airTemperature")) isTrue

                    assertThat (backend.ContainsKey(camelProps, "windSpeed")) isTrue
            )
        ]
    )

// ============================================================================
// Refined types pull their schema (with constraints) through the registry
// ============================================================================

open Fable.TypedJson.Refined

type Account = {
    Username: NonEmptyString
    Email: Email
}

let private refinedTypesTests =
    testList (
        "Refined types pull their schema (with constraints) through the registry",
        [
            test (
                "schema of refined type fields includes constraints",
                fun _ ->
                    let codecs = emptyRegistry |> registerAll
                    let json = jsonSchemaOf<Account> codecs CaseRules.SnakeCase
                    let parsed = parseRaw json

                    let props = backend.Get(parsed, "properties")

                    // NonEmptyString carries `minLength: 1` (from `Codec.nonEmpty`).
                    let usernameSchema = backend.Get(props, "username")

                    assertThat (getString backend usernameSchema "type") (isEqualTo "string")

                    assertThat (getInt backend usernameSchema "minLength") (isEqualTo 1)

                    // Email carries `pattern` (from `Codec.pattern`).
                    let emailSchema = backend.Get(props, "email")

                    assertThat (getString backend emailSchema "type") (isEqualTo "string")

                    assertThat (backend.ContainsKey(emailSchema, "pattern")) isTrue
            )
        ]
    )

let tests =
    testList (
        "JsonSchema",
        [
            primitivesRequiredFieldsTests
            nestedRecordsTests
            listsTests
            caseRulesPropertyKeysTests
            refinedTypesTests
        ]
    )
