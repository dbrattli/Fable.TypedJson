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

[<Fact>]
let ``test gt rejects values at threshold`` () =
    let codec = Codec.int |> Codec.gt 0

    match codec.Decode(JInt 0) with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg |> equal "must be > 0"

[<Fact>]
let ``test gt accepts above threshold`` () =
    let codec = Codec.int |> Codec.gt 0

    match codec.Decode(JInt 1) with
    | Ok n -> n |> equal 1
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test stacked gt and le bounds`` () =
    let codec = Codec.int |> Codec.gt 0 |> Codec.le 14

    match codec.Decode(JInt 14) with
    | Ok n -> n |> equal 14
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test stacked gt and le rejects above upper`` () =
    let codec = Codec.int |> Codec.gt 0 |> Codec.le 14

    match codec.Decode(JInt 15) with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg |> equal "must be <= 14"

[<Fact>]
let ``test minLength rejects shorter strings`` () =
    let codec = Codec.string |> Codec.minLength 3

    match codec.Decode(JString "ab") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg |> equal "must have length >= 3"

[<Fact>]
let ``test nonEmpty accepts non-empty`` () =
    let codec = Codec.string |> Codec.nonEmpty

    match codec.Decode(JString "x") with
    | Ok s -> s |> equal "x"
    | Error _ -> equal "Ok" "Error"

[<Fact>]
let ``test nonEmpty rejects empty`` () =
    let codec = Codec.string |> Codec.nonEmpty

    match codec.Decode(JString "") with
    | Ok _ -> equal "Error" "Ok"
    | Error msg -> msg |> equal "must be non-empty"

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

[<Fact>]
let ``test auto with custom codec accepts valid value`` () =
    let codec =
        autoWith<Req> codecs
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"days":7, "name":"hello"}"""

    match codec.decode map with
    | Ok r ->
        let (DayCount n) = r.Days
        n |> equal 7
        r.Name |> equal "hello"
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test auto with custom codec rejects out-of-range value`` () =
    let codec =
        autoWith<Req> codecs
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"days":15, "name":"hello"}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        // The error from Codec.le surfaces through the registry into the FieldError list.
        let formatted = formatErrors errs
        formatted.Contains("days") |> equal true
        formatted.Contains("must be <= 14") |> equal true

[<Fact>]
let ``test auto with custom codec rejects zero`` () =
    let codec =
        autoWith<Req> codecs
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"days":0, "name":"hello"}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs
        formatted.Contains("must be > 0") |> equal true

// ============================================================================
// withModel — cross-field validator (Pydantic @model_validator)
// ============================================================================

// `End` is a reserved word in Erlang and Fable BEAM mangles record field
// names that conflict with reserved words by appending `_`, so we use
// `Until` here to keep the test cross-backend.
type Range = { Start: int; Until: int }

[<Fact>]
let ``test withModel accepts valid cross-field invariant`` () =
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
        r.Start |> equal 1
        r.Until |> equal 10
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test withModel rejects invalid cross-field invariant`` () =
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
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs

        formatted.Contains("start must precede end")
        |> equal true
