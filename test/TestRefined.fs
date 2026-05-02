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

#if PYTHON
open Fable.TypedJson.Python.Json
#else
#if JS
open Fable.TypedJson.JS.Json
#else
open Fable.TypedJson.Beam.Json
#endif
#endif

// ============================================================================
// NonEmptyString
// ============================================================================

[<Fact>]
let ``test NonEmptyString accepts non-empty`` () =
    match NonEmptyString.JsonCodec.Decode(JString "hello") with
    | Ok(NonEmptyString s) -> s |> equal "hello"
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test NonEmptyString rejects empty`` () =
    match NonEmptyString.JsonCodec.Decode(JString "") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg |> equal "must be non-empty"

// ============================================================================
// PositiveInt
// ============================================================================

[<Fact>]
let ``test PositiveInt accepts 1`` () =
    match PositiveInt.JsonCodec.Decode(JInt 1) with
    | Ok(PositiveInt n) -> n |> equal 1
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test PositiveInt rejects 0`` () =
    match PositiveInt.JsonCodec.Decode(JInt 0) with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg.Contains("must be > 0") |> equal true

[<Fact>]
let ``test PositiveInt rejects negative`` () =
    match PositiveInt.JsonCodec.Decode(JInt -5) with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg.Contains("must be > 0") |> equal true

// ============================================================================
// NonNegativeInt
// ============================================================================

[<Fact>]
let ``test NonNegativeInt accepts 0`` () =
    match NonNegativeInt.JsonCodec.Decode(JInt 0) with
    | Ok(NonNegativeInt n) -> n |> equal 0
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test NonNegativeInt rejects -1`` () =
    match NonNegativeInt.JsonCodec.Decode(JInt -1) with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg.Contains("must be >= 0") |> equal true

// ============================================================================
// Email
// ============================================================================

[<Fact>]
let ``test Email accepts valid address`` () =
    match Email.JsonCodec.Decode(JString "user@example.com") with
    | Ok(Email s) -> s |> equal "user@example.com"
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test Email rejects no at-sign`` () =
    match Email.JsonCodec.Decode(JString "userexample.com") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg.Contains("must match pattern") |> equal true

[<Fact>]
let ``test Email rejects no domain dot`` () =
    match Email.JsonCodec.Decode(JString "user@example") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg.Contains("must match pattern") |> equal true

// ============================================================================
// Url
// ============================================================================

[<Fact>]
let ``test Url accepts https`` () =
    match Url.JsonCodec.Decode(JString "https://example.com") with
    | Ok(Url s) -> s |> equal "https://example.com"
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test Url accepts http`` () =
    match Url.JsonCodec.Decode(JString "http://example.com") with
    | Ok(Url s) -> s |> equal "http://example.com"
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test Url rejects ftp`` () =
    match Url.JsonCodec.Decode(JString "ftp://example.com") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg.Contains("must match pattern") |> equal true

// ============================================================================
// Uuid
// ============================================================================

[<Fact>]
let ``test Uuid accepts valid v4-shaped`` () =
    match Uuid.JsonCodec.Decode(JString "550e8400-e29b-41d4-a716-446655440000") with
    | Ok(Uuid s) -> s |> equal "550e8400-e29b-41d4-a716-446655440000"
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test Uuid rejects malformed`` () =
    match Uuid.JsonCodec.Decode(JString "not-a-uuid") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg.Contains("must match pattern") |> equal true

// ============================================================================
// End-to-end with auto<'T>: refined types as record fields
// ============================================================================

type Account = {
    Username: NonEmptyString
    Age: NonNegativeInt
    Contact: Email
}

let private codecs: CodecRegistry = emptyRegistry |> registerAll

[<Fact>]
let ``test auto with refined types accepts valid record`` () =
    let codec =
        autoWith<Account> codecs
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"username":"alice","age":30,"contact":"alice@example.com"}"""

    match codec.decode map with
    | Ok r ->
        let (NonEmptyString u) = r.Username
        u |> equal "alice"
        let (NonNegativeInt a) = r.Age
        a |> equal 30
        let (Email e) = r.Contact
        e |> equal "alice@example.com"
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test auto with refined types rejects empty username`` () =
    let codec =
        autoWith<Account> codecs
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"username":"","age":30,"contact":"alice@example.com"}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs

        formatted.Contains("must be non-empty")
        |> equal true

[<Fact>]
let ``test auto with refined types rejects negative age`` () =
    let codec =
        autoWith<Account> codecs
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"username":"alice","age":-1,"contact":"alice@example.com"}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs
        formatted.Contains("must be >= 0") |> equal true

[<Fact>]
let ``test auto with refined types rejects malformed email`` () =
    let codec =
        autoWith<Account> codecs
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"username":"alice","age":30,"contact":"not-an-email"}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs

        formatted.Contains("must match pattern")
        |> equal true
