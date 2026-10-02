(**
# TestStrict — Opt-in strict JSON decoding

Strictness applies only to parsed JSON. String-map inputs remain coercing,
because every value in that source is text by construction.

invariant: `auto` remains coercing while `autoStrict` rejects cross-type JSON primitives
invariant: strict failures retain the same nested paths and field accumulation as ordinary decoding
*)

module Fable.TypedJson.Tests.Strict

open Fable.TypedJson
open Fable.TypedJson.Json
open Fable.TypedJson.Refined
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

type Scalars = {
    Count: int
    Ratio: float
    Label: string
    Enabled: bool
}

type Child = { Count: int }

type Int64Field = { Count: int64 }

type Nested = {
    Child: Child
    Children: Child list
    Name: string
}

type Payload = { Count: int }

type Command =
    | Ping
    | Update of Payload

type WrappedInt =
    | WrappedInt of int

    static member JsonCodec: IJsonCodec<WrappedInt> =
        Codec.int
        |> Codec.map WrappedInt (fun (WrappedInt value) -> value)

type Registered = { Value: WrappedInt }

type RefinedRecord = {
    Age: NonNegativeInt
    Name: NonEmptyString
}

let private expectErrorPaths expected result =
    match result with
    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
    | Error errors ->
        let paths = errors |> List.map _.path
        assertThat paths (isEqualTo expected)

let private primitiveTests =
    testList (
        "Primitive strictness",
        [
            test (
                "default auto keeps compatible coercion",
                fun _ ->
                    let json = """{"count":"3","ratio":"2.5","label":4,"enabled":"true"}"""

                    match (auto<Scalars>()).decode(parseRaw json) with
                    | Error errors -> assertThat (formatErrors errors) (isEqualTo "Ok")
                    | Ok value ->
                        assertThat value.Count (isEqualTo 3)
                        assertThat value.Ratio (isEqualTo 2.5)
                        assertThat value.Label (isEqualTo "4")
                        assertThat value.Enabled isTrue
            )
            test (
                "strict rejects cross-type primitives and accepts an integral number for float",
                fun _ ->
                    let json = """{"count":"3","ratio":2,"label":4,"enabled":"true"}"""

                    (autoStrict<Scalars>()).decode(parseRaw json)
                    |> expectErrorPaths [ "count"; "label"; "enabled" ]
            )
            test (
                "strict rejects a fractional number for int",
                fun _ ->
                    let json = """{"count":3.9,"ratio":2.0,"label":"ok","enabled":true}"""

                    (autoStrict<Scalars>()).decode(parseRaw json)
                    |> expectErrorPaths [ "count" ]
            )
            test (
                "strict codec keeps string-map decoding coercing",
                fun _ ->
                    let input =
                        Map.ofList [ "count", "3"; "ratio", "2.5"; "label", "ok"; "enabled", "true" ]

                    match (autoStrict<Scalars>()).decodeStringMap input with
                    | Error errors -> assertThat (formatErrors errors) (isEqualTo "Ok")
                    | Ok value ->
                        assertThat value.Count (isEqualTo 3)
                        assertThat value.Enabled isTrue
            )
        ]
    )

let private integerBoundsTests =
    testList (
        "Strict integer bounds",
        [
            test (
                "int rejects numbers outside Int32 bounds",
                fun _ ->
                    let codec = autoStrict<Child>()

                    for number in [ "2147483648"; "-2147483649"; "2147483648.0"; "-2147483649.0"; "1e100" ] do
                        codec.decode (parseRaw ("{\"count\":" + number + "}"))
                        |> expectErrorPaths [ "count" ]
            )
            test (
                "int accepts both Int32 bounds as integer and floating-point input",
                fun _ ->
                    let codec = autoStrict<Child>()

                    for number, expected in
                        [
                            "2147483647", System.Int32.MaxValue
                            "-2147483648", System.Int32.MinValue
                            "2147483647.0", System.Int32.MaxValue
                            "-2147483648.0", System.Int32.MinValue
                        ] do
                        match codec.decode (parseRaw ("{\"count\":" + number + "}")) with
                        | Error errors -> assertThat (formatErrors errors) (isEqualTo "Ok")
                        | Ok value -> assertThat value.Count (isEqualTo expected)
            )
            test (
                "int64 rejects numbers outside Int64 bounds and fractional input",
                fun _ ->
                    let codec = autoStrict<Int64Field>()

                    for number in
                        [
                            "9223372036854775808"
                            "-9223372036854777856"
                            "9223372036854775808.0"
                            "-9223372036854777856.0"
                            "1e100"
                            "3.9"
                        ] do
                        codec.decode (parseRaw ("{\"count\":" + number + "}"))
                        |> expectErrorPaths [ "count" ]
            )
            test (
                "int64 accepts representable numbers at the bounds and beyond Int32",
                fun _ ->
                    let codec = autoStrict<Int64Field>()

                    for number, expected in
                        [
                            "-9223372036854775808", System.Int64.MinValue
                            "-9223372036854775808.0", System.Int64.MinValue
                            "9223372036854774784", 9223372036854774784L
                            "9223372036854774784.0", 9223372036854774784L
                            "2147483648", 2147483648L
                            "2147483648.0", 2147483648L
                            "2147483647", 2147483647L
                            "-2147483648", -2147483648L
                            "-2147483649", -2147483649L
                            "0", 0L
                            "42", 42L
                        ] do
                        match codec.decode (parseRaw ("{\"count\":" + number + "}")) with
                        | Error errors -> assertThat (formatErrors errors) (isEqualTo "Ok")
                        | Ok value -> assertThat value.Count (isEqualTo expected)
            )
#if !JS && !DOTNET
            test (
                "int64 checks exact native integer bounds",
                fun _ ->
                    let codec = autoStrict<Int64Field>()

                    for number in [ "9223372036854775808"; "-9223372036854775809" ] do
                        codec.decode (parseRaw ("{\"count\":" + number + "}"))
                        |> expectErrorPaths [ "count" ]

                    match codec.decode (parseRaw """{"count":9223372036854775807}""") with
                    | Error errors -> assertThat (formatErrors errors) (isEqualTo "Ok")
                    | Ok value -> assertThat value.Count (isEqualTo System.Int64.MaxValue)
            )
#endif
        ]
    )

let private structuralTests =
    testList (
        "Structural strictness",
        [
            test (
                "nested record and list errors accumulate with full paths",
                fun _ ->
                    let json = """{"child":{"count":"1"},"children":[{"count":"2"}],"name":3}"""

                    (autoStrict<Nested>()).decode(parseRaw json)
                    |> expectErrorPaths [ "child.count"; "children[0].count"; "name" ]
            )
            test (
                "strict decoding reaches union payload fields",
                fun _ ->
                    let json = """{"type":"update","count":"3"}"""

                    (autoStrict<Command>()).decode(parseRaw json)
                    |> expectErrorPaths [ "count" ]
            )
        ]
    )

let private registeredCodecTests =
    testList (
        "Registered codec strictness",
        [
            test (
                "registered codec follows its declared integer schema",
                fun _ ->
                    let registry = emptyRegistry |> register WrappedInt.JsonCodec
                    let json = """{"value":"3"}"""

                    (autoStrictWith<Registered> registry).decode(parseRaw json)
                    |> expectErrorPaths [ "value" ]
            )
            test (
                "bundled refined codecs reject cross-type JSON values",
                fun _ ->
                    let registry = emptyRegistry |> registerAll
                    let json = """{"age":"3","name":4}"""

                    (autoStrictWith<RefinedRecord> registry).decode(parseRaw json)
                    |> expectErrorPaths [ "age"; "name" ]
            )
            test (
                "registered codecs still coerce string-map values",
                fun _ ->
                    let registry = emptyRegistry |> register WrappedInt.JsonCodec
                    let input = Map.ofList [ "value", "3" ]

                    match (autoStrictWith<Registered> registry).decodeStringMap input with
                    | Error errors -> assertThat (formatErrors errors) (isEqualTo "Ok")
                    | Ok value ->
                        let (WrappedInt n) = value.Value
                        assertThat n (isEqualTo 3)
            )
        ]
    )

let tests =
    testList ("Strict JSON", [ primitiveTests; integerBoundsTests; structuralTests; registeredCodecTests ])
