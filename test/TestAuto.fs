(**
# TestAuto — Tests for TypedJson.auto reflection-based codec

Tests that auto<'T> correctly decodes JSON into F# records
and encodes records back to JSON, using FSharp.Reflection.
*)

module Fable.TypedJson.Tests.Auto

open Fable.TypedJson.Testing
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
// Test Record Types
// ============================================================================

type SimpleRecord = { Name: string; Age: int }

type RecordWithFloat = {
    AirTemperature: float
    RelativeHumidity: float
}

type RecordWithOption = { Name: string; Email: string option }

type RecordWithBool = { Active: bool; Count: int }

(**
adr: lowercase-first fields kept as their own types — every other test record is PascalCase, which cannot catch BEAM reflection drift
assumption: Fable BEAM keys a record map with `sanitizeFieldName` and reflection's `erl_name` derives from the same function
*)
type LowerFirst = { alpha: string; beta: int }

type LowerFirstMultiWord = {
    airTemperature: float
    relativeHumidity: float
}

// ============================================================================
// Decode Tests
// ============================================================================

let private decodeTests =
    testList (
        "Decode",
        [
            test (
                "auto decode simple record",
                fun _ ->
                    let codec =
                        auto<SimpleRecord> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"name":"Alice","age":30}"""

                    match codec.decode map with
                    | Ok record ->
                        assertThat record.Name (isEqualTo "Alice")
                        assertThat record.Age (isEqualTo 30)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
            test (
                "auto decode float record",
                fun _ ->
                    let codec =
                        auto<RecordWithFloat> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"air_temperature":22.5,"relative_humidity":65.0}"""

                    match codec.decode map with
                    | Ok record ->
                        assertThat record.AirTemperature (isEqualTo 22.5)
                        assertThat record.RelativeHumidity (isEqualTo 65.0)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
            test (
                "auto decode with option some",
                fun _ ->
                    let codec =
                        auto<RecordWithOption> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"name":"Alice","email":"a@b.com"}"""

                    match codec.decode map with
                    | Ok record ->
                        assertThat record.Name (isEqualTo "Alice")
                        assertThat record.Email (isEqualTo (Some "a@b.com"))
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
            test (
                "auto decode with option none",
                fun _ ->
                    let codec =
                        auto<RecordWithOption> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"name":"Alice"}"""

                    match codec.decode map with
                    | Ok record ->
                        assertThat record.Name (isEqualTo "Alice")
                        assertThat record.Email (isEqualTo None)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
            test (
                "auto decode missing required field",
                fun _ ->
                    let codec =
                        auto<SimpleRecord> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"name":"Alice"}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "age")
            )
            test (
                "auto decode accumulates all errors",
                fun _ ->
                    let codec =
                        auto<SimpleRecord> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors -> assertThat errors.Length (isEqualTo 2)
            )
        ]
    )

// ============================================================================
// Encode Tests
// ============================================================================

let private encodeTests =
    testList (
        "Encode",
        [
            test (
                "auto encode simple record",
                fun _ ->
                    let codec =
                        auto<SimpleRecord> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let record = { Name = "Bob"; Age = 25 }
                    let json = codec.encode record
                    let map = parseRaw json
                    let name = getString backend map "name"
                    let age = getInt backend map "age"
                    assertThat name (isEqualTo "Bob")
                    assertThat age (isEqualTo 25)
            )
            test (
                "auto encode float record",
                fun _ ->
                    let codec =
                        auto<RecordWithFloat> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let record = {
                        AirTemperature = 22.5
                        RelativeHumidity = 65.0
                    }

                    let json = codec.encode record
                    let map = parseRaw json
                    let temp = getFloat backend map "air_temperature"
                    let humidity = getFloat backend map "relative_humidity"
                    assertThat temp (isEqualTo 22.5)
                    assertThat humidity (isEqualTo 65.0)
            )
            test (
                "auto encode with option some",
                fun _ ->
                    let codec =
                        auto<RecordWithOption> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let record = {
                        Name = "Alice"
                        Email = Some "a@b.com"
                    }

                    let json = codec.encode record
                    let map = parseRaw json
                    let name = getString backend map "name"
                    let email = getString backend map "email"
                    assertThat name (isEqualTo "Alice")
                    assertThat email (isEqualTo "a@b.com")
            )
        ]
    )

// ============================================================================
// Round-trip Tests
// ============================================================================

let private roundTripTests =
    testList (
        "Round-trip",
        [
            test (
                "auto round-trip simple record",
                fun _ ->
                    let codec =
                        auto<SimpleRecord> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let original = { Name = "Charlie"; Age = 40 }
                    let json = codec.encode original
                    let map = parseRaw json

                    match codec.decode map with
                    | Ok decoded ->
                        assertThat decoded.Name (isEqualTo original.Name)
                        assertThat decoded.Age (isEqualTo original.Age)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
            test (
                "auto round-trip float record",
                fun _ ->
                    let codec =
                        auto<RecordWithFloat> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let original = {
                        AirTemperature = -5.3
                        RelativeHumidity = 88.2
                    }

                    let json = codec.encode original
                    let map = parseRaw json

                    match codec.decode map with
                    | Ok decoded ->
                        assertThat decoded.AirTemperature (isEqualTo original.AirTemperature)

                        assertThat decoded.RelativeHumidity (isEqualTo original.RelativeHumidity)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// Lowercase-first Field Tests
// ============================================================================

(**
Regression guard for fable-compiler/Fable#4849 (fixed in Fable 5.13.0): BEAM keyed a
record map with `sanitizeFieldName` (trailing `_` on lowercase-first names) while
reflection metadata derived `erl_name` with `sanitizeErlangName` (no underscore). Encode
crashed with `{badkey,alpha}`; decode built a map the compiled accessor could not read.

invariant: `auto<'T>` handles lowercase-first record fields identically on all four targets
adr: decode asserts through the compiled accessor (`decoded.alpha`), not reflection — that is the direction MakeRecord corrupts silently
adr: JSON keys stay derived from `PropertyInfo.Name`, so the BEAM disambiguating `_` never reaches the wire
*)
let private lowercaseFirstTests =
    testList (
        "Lowercase-first fields",
        [
            test (
                "auto decode lowercase-first record",
                fun _ ->
                    let codec =
                        auto<LowerFirst> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"alpha":"a","beta":1}"""

                    match codec.decode map with
                    | Ok record ->
                        assertThat record.alpha (isEqualTo "a")
                        assertThat record.beta (isEqualTo 1)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
            test (
                "auto encode lowercase-first record",
                fun _ ->
                    let codec =
                        auto<LowerFirst> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let json = codec.encode { alpha = "a"; beta = 1 }
                    let map = parseRaw json
                    assertThat (getString backend map "alpha") (isEqualTo "a")
                    assertThat (getInt backend map "beta") (isEqualTo 1)
            )
            test (
                "auto round-trip camelCase multi-word record",
                fun _ ->
                    let codec =
                        auto<LowerFirstMultiWord> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let original = {
                        airTemperature = 22.5
                        relativeHumidity = 65.0
                    }

                    let json = codec.encode original
                    let map = parseRaw json
                    assertThat (getFloat backend map "air_temperature") (isEqualTo 22.5)

                    match codec.decode map with
                    | Ok decoded ->
                        assertThat decoded.airTemperature (isEqualTo original.airTemperature)
                        assertThat decoded.relativeHumidity (isEqualTo original.relativeHumidity)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
        ]
    )

let tests =
    testList ("Auto", [ decodeTests; encodeTests; roundTripTests; lowercaseFirstTests ])
