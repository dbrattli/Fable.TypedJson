(**
# TestRefined — Bundled refined types

Verifies the validators-as-types instances ship correctly:
NonEmptyString, PositiveInt, NonNegativeInt, Email, Url, Uuid.
*)

module Fable.TypedJson.Tests.Refined

open Fable.TypedJson.Testing
open Fable.TypedJson.Schema
open Fable.TypedJson.Refined
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
// NonEmptyString
// ============================================================================

let private nonEmptyStringTests =
    testList (
        "NonEmptyString",
        [
            test (
                "NonEmptyString accepts non-empty",
                fun _ ->
                    match NonEmptyString.JsonCodec.Decode(JString "hello") with
                    | Ok(NonEmptyString s) -> assertThat s (isEqualTo "hello")
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "NonEmptyString rejects empty",
                fun _ ->
                    match NonEmptyString.JsonCodec.Decode(JString "") with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat msg (isEqualTo "must be non-empty")
            )
        ]
    )

// ============================================================================
// PositiveInt
// ============================================================================

let private positiveIntTests =
    testList (
        "PositiveInt",
        [
            test (
                "PositiveInt accepts 1",
                fun _ ->
                    match PositiveInt.JsonCodec.Decode(JInt 1) with
                    | Ok(PositiveInt n) -> assertThat n (isEqualTo 1)
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "PositiveInt rejects 0",
                fun _ ->
                    match PositiveInt.JsonCodec.Decode(JInt 0) with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat (msg.Contains("must be > 0")) isTrue
            )
            test (
                "PositiveInt rejects negative",
                fun _ ->
                    match PositiveInt.JsonCodec.Decode(JInt -5) with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat (msg.Contains("must be > 0")) isTrue
            )
        ]
    )

// ============================================================================
// NonNegativeInt
// ============================================================================

let private nonNegativeIntTests =
    testList (
        "NonNegativeInt",
        [
            test (
                "NonNegativeInt accepts 0",
                fun _ ->
                    match NonNegativeInt.JsonCodec.Decode(JInt 0) with
                    | Ok(NonNegativeInt n) -> assertThat n (isEqualTo 0)
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "NonNegativeInt rejects -1",
                fun _ ->
                    match NonNegativeInt.JsonCodec.Decode(JInt -1) with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat (msg.Contains("must be >= 0")) isTrue
            )
        ]
    )

// ============================================================================
// Email
// ============================================================================

let private emailTests =
    testList (
        "Email",
        [
            test (
                "Email accepts valid address",
                fun _ ->
                    match Email.JsonCodec.Decode(JString "user@example.com") with
                    | Ok(Email s) -> assertThat s (isEqualTo "user@example.com")
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "Email rejects no at-sign",
                fun _ ->
                    match Email.JsonCodec.Decode(JString "userexample.com") with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat (msg.Contains("must match pattern")) isTrue
            )
            test (
                "Email rejects no domain dot",
                fun _ ->
                    match Email.JsonCodec.Decode(JString "user@example") with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat (msg.Contains("must match pattern")) isTrue
            )
        ]
    )

// ============================================================================
// Url
// ============================================================================

let private urlTests =
    testList (
        "Url",
        [
            test (
                "Url accepts https",
                fun _ ->
                    match Url.JsonCodec.Decode(JString "https://example.com") with
                    | Ok(Url s) -> assertThat s (isEqualTo "https://example.com")
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "Url accepts http",
                fun _ ->
                    match Url.JsonCodec.Decode(JString "http://example.com") with
                    | Ok(Url s) -> assertThat s (isEqualTo "http://example.com")
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "Url rejects ftp",
                fun _ ->
                    match Url.JsonCodec.Decode(JString "ftp://example.com") with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat (msg.Contains("must match pattern")) isTrue
            )
        ]
    )

// ============================================================================
// Uuid
// ============================================================================

let private uuidTests =
    testList (
        "Uuid",
        [
            test (
                "Uuid accepts valid v4-shaped",
                fun _ ->
                    match Uuid.JsonCodec.Decode(JString "550e8400-e29b-41d4-a716-446655440000") with
                    | Ok(Uuid s) -> assertThat s (isEqualTo "550e8400-e29b-41d4-a716-446655440000")
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "Uuid rejects malformed",
                fun _ ->
                    match Uuid.JsonCodec.Decode(JString "not-a-uuid") with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat (msg.Contains("must match pattern")) isTrue
            )
        ]
    )

// ============================================================================
// End-to-end with auto<'T>: refined types as record fields
// ============================================================================

type Account = {
    Username: NonEmptyString
    Age: NonNegativeInt
    Contact: Email
}

let private codecs: CodecRegistry = emptyRegistry |> registerAll

let private endToEndTests =
    testList (
        "End-to-end with auto<'T>: refined types as record fields",
        [
            test (
                "auto with refined types accepts valid record",
                fun _ ->
                    let codec =
                        autoWith<Account> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"username":"alice","age":30,"contact":"alice@example.com"}"""

                    match codec.decode map with
                    | Ok r ->
                        let (NonEmptyString u) = r.Username
                        assertThat u (isEqualTo "alice")
                        let (NonNegativeInt a) = r.Age
                        assertThat a (isEqualTo 30)
                        let (Email e) = r.Contact
                        assertThat e (isEqualTo "alice@example.com")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "auto with refined types rejects empty username",
                fun _ ->
                    let codec =
                        autoWith<Account> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"username":"","age":30,"contact":"alice@example.com"}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs

                        assertThat (formatted.Contains("must be non-empty")) isTrue
            )
            test (
                "auto with refined types rejects negative age",
                fun _ ->
                    let codec =
                        autoWith<Account> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"username":"alice","age":-1,"contact":"alice@example.com"}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs
                        assertThat (formatted.Contains("must be >= 0")) isTrue
            )
            test (
                "auto with refined types rejects malformed email",
                fun _ ->
                    let codec =
                        autoWith<Account> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"username":"alice","age":30,"contact":"not-an-email"}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs

                        assertThat (formatted.Contains("must match pattern")) isTrue
            )
        ]
    )

// ============================================================================
// Refined types encode back to their underlying value
// ============================================================================

(**
The bundled refined types are single-case DUs, so without a registry check on
the encode path the tagged-DU branch claims them and emits
`{"username": {"type": "non_empty_string"}}` — the value is silently lost, and
a record that decoded cleanly cannot be re-encoded.

invariant: a refined field encodes as its underlying primitive, never as a tagged DU
*)
let private refinedEncodeTests =
    testList (
        "Refined types encode back to their underlying value",
        [
            test (
                "refined fields encode as their underlying primitives",
                fun _ ->
                    let codec =
                        autoWith<Account> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let json =
                        codec.encode {
                            Username = NonEmptyString "alice"
                            Age = NonNegativeInt 30
                            Contact = Email "alice@example.com"
                        }

                    let parsed = parseRaw json
                    assertThat (getString backend parsed "username") (isEqualTo "alice")
                    assertThat (getInt backend parsed "age") (isEqualTo 30)
                    assertThat (getString backend parsed "contact") (isEqualTo "alice@example.com")
            )
            test (
                "record of refined types round-trips",
                fun _ ->
                    let codec =
                        autoWith<Account> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let original = {
                        Username = NonEmptyString "alice"
                        Age = NonNegativeInt 30
                        Contact = Email "alice@example.com"
                    }

                    match codec.decode (parseRaw (codec.encode original)) with
                    | Ok r ->
                        let (NonEmptyString u) = r.Username
                        assertThat u (isEqualTo "alice")
                        let (NonNegativeInt a) = r.Age
                        assertThat a (isEqualTo 30)
                        let (Email e) = r.Contact
                        assertThat e (isEqualTo "alice@example.com")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
        ]
    )

let tests =
    testList (
        "Refined",
        [
            nonEmptyStringTests
            positiveIntTests
            nonNegativeIntTests
            emailTests
            urlTests
            uuidTests
            endToEndTests
            refinedEncodeTests
        ]
    )
