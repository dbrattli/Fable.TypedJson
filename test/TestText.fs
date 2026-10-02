(**
# TestText — Safe JSON text decoding

invariant: parser failures become `InvalidText` and typed failures become `InvalidValue`
invariant: decoder and validator defects cross the `decodeText` boundary as exceptions
*)

module Fable.TypedJson.Tests.Text

open Fable.TypedJson
open Fable.TypedJson.Json
open Fable.TypedJson.Schema
open Fable.TypedJson.Testing
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

type Input = { Count: int }

type Defective =
    | Defective of string

    static member JsonCodec: IJsonCodec<Defective> =
        Codec.mk (fun _ -> failwith "custom decoder defect") (fun (Defective value) -> JString value) (primitiveSchema "string")

type DefectiveInput = { Value: Defective }

let private testsForResults =
    testList (
        "Text error results",
        [
            test (
                "valid text decodes",
                fun _ ->
                    match decodeText (auto<Input>()) """{"count":3}""" with
                    | Ok value -> assertThat value.Count (isEqualTo 3)
                    | Error _ -> assertThat "Error" (isEqualTo "Ok")
            )
            test (
                "invalid JSON is an InvalidText error",
                fun _ ->
                    match decodeText (auto<Input>()) "{" with
                    | Error(InvalidText message) -> assertThat (System.String.IsNullOrEmpty message) isFalse
                    | Error(InvalidValue _) -> assertThat "InvalidValue" (isEqualTo "InvalidText")
                    | Ok _ -> assertThat "Ok" (isEqualTo "InvalidText")
            )
            test (
                "typed decoding errors are InvalidValue errors",
                fun _ ->
                    match decodeText (autoStrict<Input>()) """{"count":"3"}""" with
                    | Error(InvalidValue [ error ]) -> assertThat error.path (isEqualTo "count")
                    | Error(InvalidValue errors) -> assertThat errors.Length (isEqualTo 1)
                    | Error(InvalidText _) -> assertThat "InvalidText" (isEqualTo "InvalidValue")
                    | Ok _ -> assertThat "Ok" (isEqualTo "InvalidValue")
            )
        ]
    )

let private exceptionBoundaryTests =
    testList (
        "Exception boundary",
        [
            test (
                "custom codec defects still throw",
                fun _ ->
                    let registry = emptyRegistry |> register Defective.JsonCodec
                    let mutable threw = false

                    try
                        decodeText (autoWith<DefectiveInput> registry) """{"value":"x"}"""
                        |> ignore
                    with _ ->
                        threw <- true

                    assertThat threw isTrue
            )
            test (
                "model validator defects still throw",
                fun _ ->
                    let codec =
                        auto<Input>()
                        |> withModel (fun _ -> failwith "model validator defect")

                    let mutable threw = false

                    try
                        decodeText codec """{"count":3}""" |> ignore
                    with _ ->
                        threw <- true

                    assertThat threw isTrue
            )
        ]
    )

let tests = testList ("JSON text", [ testsForResults; exceptionBoundaryTests ])
