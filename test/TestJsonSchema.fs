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

// ============================================================================
// Recursive types terminate
// ============================================================================

(**
`schemaForRecord` is defined in terms of its fields' schemas, so a record that
reaches itself has no natural base case — before the cycle guard these types
recursed until the stack blew. On BEAM the failure was previously masked:
`typeof<Tree>` killed the *compiler* (fable-compiler/Fable#4766, fixed in Fable
5.8.1), so the emitter was never reached. These tests are the regression guard
for both.

invariant: a record already on the path back to the root emits `{type, title}` and nothing deeper
adr: the diamond test is what pins `visited` to the path — a global accumulator would truncate `Right`
*)
type Tree = { Label: string; Children: Tree list }

type Node = { Name: string; Next: Node option }

type Parent = { Tag: string; Child: Child option }

and Child = { Kind: string; Owner: Parent option }

type Leaf = { Value: int }

type Diamond = { Left: Leaf; Right: Leaf }

let private recursiveTypesTests =
    testList (
        "Recursive types terminate",
        [
            test (
                "self-reference through a list truncates",
                fun _ ->
                    let json = jsonSchemaOf<Tree> emptyRegistry CaseRules.SnakeCase
                    let parsed = parseRaw json

                    let props = backend.Get(parsed, "properties")

                    // The non-recursive field is unaffected.
                    assertThat (getString backend (backend.Get(props, "label")) "type") (isEqualTo "string")

                    let children = backend.Get(props, "children")
                    assertThat (getString backend children "type") (isEqualTo "array")

                    // `items` is Tree again — named, but not expanded.
                    let items = backend.Get(children, "items")
                    assertThat (getString backend items "type") (isEqualTo "object")
                    assertThat (getString backend items "title") (isEqualTo "Tree")
                    assertThat (backend.ContainsKey(items, "properties")) isFalse
            )
            test (
                "self-reference through an option truncates and stays optional",
                fun _ ->
                    let json = jsonSchemaOf<Node> emptyRegistry CaseRules.SnakeCase
                    let parsed = parseRaw json

                    let props = backend.Get(parsed, "properties")
                    let next = backend.Get(props, "next")

                    assertThat (getString backend next "type") (isEqualTo "object")
                    assertThat (getString backend next "title") (isEqualTo "Node")
                    assertThat (backend.ContainsKey(next, "properties")) isFalse

                    // Truncation must not promote the field into `required`.
                    let required = backend.Get(parsed, "required")
                    assertThat (backend.ArrayLength required) (isEqualTo 1)
                    assertThat (arrayAtString backend required 0) (isEqualTo "name")
            )
            test (
                "mutual recursion truncates at the second hop",
                fun _ ->
                    let json = jsonSchemaOf<Parent> emptyRegistry CaseRules.SnakeCase
                    let parsed = parseRaw json

                    // Parent → Child expands fully...
                    let child = backend.Get(backend.Get(parsed, "properties"), "child")

                    assertThat (getString backend child "title") (isEqualTo "Child")

                    let childProps = backend.Get(child, "properties")
                    assertThat (getString backend (backend.Get(childProps, "kind")) "type") (isEqualTo "string")

                    // ...and Child → Parent closes the cycle, so it truncates.
                    let owner = backend.Get(childProps, "owner")
                    assertThat (getString backend owner "type") (isEqualTo "object")
                    assertThat (getString backend owner "title") (isEqualTo "Parent")
                    assertThat (backend.ContainsKey(owner, "properties")) isFalse
            )
            test (
                "repeated sibling type is not mistaken for a cycle",
                fun _ ->
                    let json = jsonSchemaOf<Diamond> emptyRegistry CaseRules.SnakeCase
                    let parsed = parseRaw json

                    let props = backend.Get(parsed, "properties")

                    // Leaf appears twice, but never on its own path — both
                    // sides expand. A global visited set would truncate one.
                    for side in [ "left"; "right" ] do
                        let leaf = backend.Get(props, side)
                        assertThat (getString backend leaf "title") (isEqualTo "Leaf")
                        assertThat (backend.ContainsKey(leaf, "properties")) isTrue

                        let value = backend.Get(backend.Get(leaf, "properties"), "value")
                        assertThat (getString backend value "type") (isEqualTo "integer")
            )
        ]
    )

// ============================================================================
// Tagged unions emit oneOf
// ============================================================================

(**
Decode and encode have handled tagged DUs since 0.4.0, but the schema emitter
was a separate walker with no union branch and fell through to a permissive
`{}` — a validator built from the schema accepted documents the codec would
reject. Now that all three faces come off one plan node, a union that decodes
is a union that has a schema.

invariant: every case the decoder accepts appears as a `oneOf` branch with its discriminator pinned
*)
type Query = { Text: string }

type Command =
    | Search of Query
    | Refresh

let private unionSchemaTests =
    testList (
        "Tagged unions emit oneOf",
        [
            test (
                "union schema lists one branch per case",
                fun _ ->
                    let parsed = parseRaw (jsonSchemaOf<Command> emptyRegistry CaseRules.SnakeCase)
                    let branches = backend.Get(parsed, "oneOf")
                    assertThat (backend.IsArray branches) isTrue
                    assertThat (backend.ArrayLength branches) (isEqualTo 2)
            )
            test (
                "a payload case pins its discriminator and flattens its fields",
                fun _ ->
                    let parsed = parseRaw (jsonSchemaOf<Command> emptyRegistry CaseRules.SnakeCase)
                    let first = backend.ArrayAt(backend.Get(parsed, "oneOf"), 0)
                    let props = backend.Get(first, "properties")

                    // Discriminator is constrained to this case's tag ...
                    assertThat (getString backend (backend.Get(props, "type")) "const") (isEqualTo "search")

                    // ... and the payload's own keys sit alongside it, matching
                    // the flattened shape the decoder reads.
                    assertThat (getString backend (backend.Get(props, "text")) "type") (isEqualTo "string")
            )
            test (
                "a fieldless case requires only the discriminator",
                fun _ ->
                    let parsed = parseRaw (jsonSchemaOf<Command> emptyRegistry CaseRules.SnakeCase)
                    let second = backend.ArrayAt(backend.Get(parsed, "oneOf"), 1)
                    let required = backend.Get(second, "required")
                    assertThat (backend.ArrayLength required) (isEqualTo 1)
                    assertThat (arrayAtString backend required 0) (isEqualTo "type")
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
            recursiveTypesTests
            unionSchemaTests
        ]
    )
