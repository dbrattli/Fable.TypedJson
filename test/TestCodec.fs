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
#else
#if JS
open Fable.TypedJson.JS.Json
#else
#if DOTNET
open Fable.TypedJson.DotNet.Json
#else
open Fable.TypedJson.Beam.Json
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
    testList ("Codec", [ pipelineCompositionTests; namedWrapperTypeTests; withModelTests ])
