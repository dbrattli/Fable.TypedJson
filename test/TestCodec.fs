(**
# TestCodec — Validators-as-types pattern

Tests that custom F# wrapper types with a static JsonCodec member (and
a one-line registration call) integrate end-to-end with `auto<'T>()`.

The Pydantic equivalents:
  Annotated[int, Field(gt=0, lt=15)]   →   Codec.int |> Codec.gt 0 |> Codec.lt 15
  class Days(BaseModel): ...           →   type Days = Days of int with static JsonCodec
*)

module Fable.TypedJson.Tests.Codec

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

// Note: we deliberately do NOT `open Fable.TypedJson.Codec` because its
// `int`, `string`, `float`, `bool` codec values would shadow F# core
// type conversion functions. Use `Codec.int` etc. throughout.
module Codec = Fable.TypedJson.Codec

// ============================================================================
// Pipeline composition (Pydantic Annotated equivalent, no new type)
// ============================================================================

let private pipelineCompositionTests =
    testList (
        "Pipeline composition (Pydantic Annotated equivalent, no new type)",
        [
            test (
                "gt rejects values at threshold",
                fun _ ->
                    let codec = Codec.int |> Codec.gt 0

                    match codec.Decode(JInt 0) with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat msg (isEqualTo "must be > 0")
            )
            test (
                "gt accepts above threshold",
                fun _ ->
                    let codec = Codec.int |> Codec.gt 0

                    match codec.Decode(JInt 1) with
                    | Ok n -> assertThat n (isEqualTo 1)
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "stacked gt and le bounds",
                fun _ ->
                    let codec = Codec.int |> Codec.gt 0 |> Codec.le 14

                    match codec.Decode(JInt 14) with
                    | Ok n -> assertThat n (isEqualTo 14)
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "stacked gt and le rejects above upper",
                fun _ ->
                    let codec = Codec.int |> Codec.gt 0 |> Codec.le 14

                    match codec.Decode(JInt 15) with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat msg (isEqualTo "must be <= 14")
            )
            test (
                "minLength rejects shorter strings",
                fun _ ->
                    let codec = Codec.string |> Codec.minLength 3

                    match codec.Decode(JString "ab") with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat msg (isEqualTo "must have length >= 3")
            )
            test (
                "nonEmpty accepts non-empty",
                fun _ ->
                    let codec = Codec.string |> Codec.nonEmpty

                    match codec.Decode(JString "x") with
                    | Ok s -> assertThat s (isEqualTo "x")
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "nonEmpty rejects empty",
                fun _ ->
                    let codec = Codec.string |> Codec.nonEmpty

                    match codec.Decode(JString "") with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error msg -> assertThat msg (isEqualTo "must be non-empty")
            )
        ]
    )

// ============================================================================
// Primitive codecs agree with Schema.coerce
// ============================================================================

(**
`Codec.int` / `int64` / `float` / `string` / `bool` and the primitive arms of
`Schema.coerce` implement the same coercion rules independently, and had
drifted apart. These pin the agreed behaviour ahead of merging the two into a
single table.

invariant: a primitive codec and the matching `coerce` arm produce the same value for the same input
*)
let private primitiveAgreementTests =
    testList (
        "Primitive codecs agree with Schema.coerce",
        [
            test (
                "float codec parses a decimal string",
                fun _ ->
                    // Pinned to InvariantCulture: on a `.`-as-thousands locale
                    // the parameterless CLR overload reads "22.5" as 225.
                    match Codec.float.Decode(JString "22.5") with
                    | Ok f -> assertThat f (isEqualTo 22.5)
                    | Error msg -> assertThat msg (isEqualTo "Ok")
            )
            test (
                "string codec renders a float without padding",
                fun _ ->
                    // `sprintf "%f"` would give "3.140000"; coerce gives "3.14".
                    match Codec.string.Decode(JFloat 3.14) with
                    | Ok s -> assertThat s (isEqualTo "3.14")
                    | Error msg -> assertThat msg (isEqualTo "Ok")
            )
            test (
                "int64 codec encodes a value beyond Int32 without wrapping",
                fun _ ->
                    // 3_000_000_000L wrapped to -1_294_967_296 through JInt.
                    match Codec.int64.Encode 3000000000L with
                    | JFloat f -> assertThat f (isEqualTo 3000000000.0)
                    | other -> assertThat (sprintf "%A" other) (isEqualTo "JFloat 3000000000.0")
            )
            test (
                "int64 codec still encodes in-range values as integers",
                fun _ ->
                    match Codec.int64.Encode 42L with
                    | JInt n -> assertThat n (isEqualTo 42)
                    | other -> assertThat (sprintf "%A" other) (isEqualTo "JInt 42")
            )
            test (
                "int64 codec round-trips a large value through a string",
                fun _ ->
                    match Codec.int64.Decode(JString "3000000000") with
                    | Ok n -> assertThat n (isEqualTo 3000000000L)
                    | Error msg -> assertThat msg (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// Named wrapper type with static JsonCodec member (registered for auto)
// ============================================================================

type DayCount =
    | DayCount of int

    static member JsonCodec: IJsonCodec<DayCount> =
        Codec.int
        |> Codec.gt 0
        |> Codec.le 14
        |> Codec.map DayCount (fun (DayCount n) -> n)

// Build a registry containing the DayCount codec; threaded explicitly into autoWith.
let private codecs: CodecRegistry = emptyRegistry |> register DayCount.JsonCodec

// PascalCase field names: a Fable BEAM codegen quirk mangles lowercase F#
// field names with a `_` suffix in field access but not in
// `make_record_from_values`, so reflection-built records can't be read.
// PascalCase fields lowercase consistently in both paths.
type Req = { Days: DayCount; Name: string }

let private namedWrapperTypeTests =
    testList (
        "Named wrapper type with static JsonCodec member (registered for auto)",
        [
            test (
                "auto with custom codec accepts valid value",
                fun _ ->
                    let codec =
                        autoWith<Req> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"days":7, "name":"hello"}"""

                    match codec.decode map with
                    | Ok r ->
                        let (DayCount n) = r.Days
                        assertThat n (isEqualTo 7)
                        assertThat r.Name (isEqualTo "hello")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "auto with custom codec rejects out-of-range value",
                fun _ ->
                    let codec =
                        autoWith<Req> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"days":15, "name":"hello"}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        // The error from Codec.le surfaces through the registry into the FieldError list.
                        let formatted = formatErrors errs
                        assertThat (formatted.Contains("days")) isTrue
                        assertThat (formatted.Contains("must be <= 14")) isTrue
            )
            test (
                "auto with custom codec rejects zero",
                fun _ ->
                    let codec =
                        autoWith<Req> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"days":0, "name":"hello"}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs
                        assertThat (formatted.Contains("must be > 0")) isTrue
            )
            (**
            A registered codec must drive BOTH directions. `register` stores an
            `encode` closure alongside `decode`, so a `DayCount` field is
            expected to encode as the underlying `7`, exactly as it decodes.

            invariant: a value that decodes through a registered codec re-encodes through the same codec
            *)
            test (
                "auto with custom codec encodes through the codec",
                fun _ ->
                    let codec =
                        autoWith<Req> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let json = codec.encode { Days = DayCount 7; Name = "hello" }
                    let parsed = parseRaw json
                    assertThat (getInt backend parsed "days") (isEqualTo 7)
                    assertThat (getString backend parsed "name") (isEqualTo "hello")
            )
            test (
                "auto with custom codec round-trips",
                fun _ ->
                    let codec =
                        autoWith<Req> codecs
                        |> withCaseRules CaseRules.SnakeCase

                    let original = { Days = DayCount 7; Name = "hello" }

                    match codec.decode (parseRaw (codec.encode original)) with
                    | Ok r ->
                        let (DayCount n) = r.Days
                        assertThat n (isEqualTo 7)
                        assertThat r.Name (isEqualTo "hello")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// withModel — cross-field validator (Pydantic @model_validator)
// ============================================================================

// `End` is a reserved word in Erlang and Fable BEAM mangles record field
// names that conflict with reserved words by appending `_`, so we use
// `Until` here to keep the test cross-backend.
type Range = { Start: int; Until: int }

let private withModelTests =
    testList (
        "withModel — cross-field validator (Pydantic @model_validator)",
        [
            test (
                "withModel accepts valid cross-field invariant",
                fun _ ->
                    let codec =
                        auto<Range> ()
                        |> withCaseRules CaseRules.SnakeCase
                        |> withModel (fun r ->
                            if r.Start <= r.Until then
                                Ok r
                            else
                                Error [
                                    {
                                        path = ""
                                        message = "start must precede end"
                                    }
                                ])

                    let map = parseRaw """{"start":1, "until":10}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Start (isEqualTo 1)
                        assertThat r.Until (isEqualTo 10)
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "withModel rejects invalid cross-field invariant",
                fun _ ->
                    let codec =
                        auto<Range> ()
                        |> withCaseRules CaseRules.SnakeCase
                        |> withModel (fun r ->
                            if r.Start <= r.Until then
                                Ok r
                            else
                                Error [
                                    {
                                        path = ""
                                        message = "start must precede end"
                                    }
                                ])

                    let map = parseRaw """{"start":10, "until":1}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs

                        assertThat (formatted.Contains("start must precede end")) isTrue
            )
        ]
    )

let tests =
    testList (
        "Codec",
        [
            pipelineCompositionTests
            primitiveAgreementTests
            namedWrapperTypeTests
            withModelTests
        ]
    )
