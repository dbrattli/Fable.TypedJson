(**
# TestCaseRules — Tests for case rule transformation

Tests that applyCaseRule correctly transforms field names
and that auto codec uses case rules for JSON key mapping.
*)

module Fable.TypedJson.Tests.CaseRules

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
// applyCaseRule Tests
// ============================================================================

let private applyCaseRuleTests =
    testList (
        "applyCaseRule",
        [
            test (
                "snake_case conversion",
                fun _ -> assertThat (applyCaseRule CaseRules.SnakeCase "AirTemperature") (isEqualTo "air_temperature")
            )
            test (
                "snake_case multi-word",
                fun _ -> assertThat (applyCaseRule CaseRules.SnakeCase "RelativeHumidity") (isEqualTo "relative_humidity")
            )
            test ("snake_case single word", fun _ -> assertThat (applyCaseRule CaseRules.SnakeCase "Name") (isEqualTo "name"))
            test ("snake_case already lowercase", fun _ -> assertThat (applyCaseRule CaseRules.SnakeCase "name") (isEqualTo "name"))
            test (
                "lower_first conversion",
                fun _ -> assertThat (applyCaseRule CaseRules.LowerFirst "AirTemperature") (isEqualTo "airTemperature")
            )
            test ("lower_first single word", fun _ -> assertThat (applyCaseRule CaseRules.LowerFirst "Name") (isEqualTo "name"))
            test (
                "kebab_case conversion",
                fun _ -> assertThat (applyCaseRule CaseRules.KebabCase "AirTemperature") (isEqualTo "air-temperature")
            )
            test (
                "snake_case_all_caps conversion",
                fun _ -> assertThat (applyCaseRule CaseRules.SnakeCaseAllCaps "AirTemperature") (isEqualTo "AIR_TEMPERATURE")
            )
            test ("none preserves case", fun _ -> assertThat (applyCaseRule CaseRules.None "AirTemperature") (isEqualTo "AirTemperature"))
            test (
                "pascal_case conversion",
                fun _ -> assertThat (applyCaseRule CaseRules.PascalCase "air_temperature") (isEqualTo "AirTemperature")
            )
            test ("pascal_case single word", fun _ -> assertThat (applyCaseRule CaseRules.PascalCase "name") (isEqualTo "Name"))
        ]
    )

// ============================================================================
// Auto Codec with Different Case Rules
// ============================================================================

type Weather = {
    AirTemperature: float
    WindSpeed: float
}

let private autoCodecCaseRulesTests =
    testList (
        "Auto Codec with Different Case Rules",
        [
            test (
                "auto decode with snake_case keys",
                fun _ ->
                    let codec = auto<Weather> ()
                    let map = parseRaw """{"air_temperature":22.5,"wind_speed":3.0}"""

                    match codec.decodeWith CaseRules.SnakeCase map with
                    | Ok w ->
                        assertThat w.AirTemperature (isEqualTo 22.5)
                        assertThat w.WindSpeed (isEqualTo 3.0)
                    | Error e -> assertThat (sprintf "Error: %A" e) (isEqualTo "Ok")
            )
            test (
                "auto decode with camelCase keys",
                fun _ ->
                    let codec = auto<Weather> ()
                    let map = parseRaw """{"airTemperature":22.5,"windSpeed":3.0}"""

                    match codec.decodeWith CaseRules.LowerFirst map with
                    | Ok w ->
                        assertThat w.AirTemperature (isEqualTo 22.5)
                        assertThat w.WindSpeed (isEqualTo 3.0)
                    | Error e -> assertThat (sprintf "Error: %A" e) (isEqualTo "Ok")
            )
            test (
                "auto decode with PascalCase keys",
                fun _ ->
                    let codec = auto<Weather> ()
                    let map = parseRaw """{"AirTemperature":22.5,"WindSpeed":3.0}"""

                    match codec.decodeWith CaseRules.PascalCase map with
                    | Ok w ->
                        assertThat w.AirTemperature (isEqualTo 22.5)
                        assertThat w.WindSpeed (isEqualTo 3.0)
                    | Error e -> assertThat (sprintf "Error: %A" e) (isEqualTo "Ok")
            )
            test (
                "auto encode with snake_case",
                fun _ ->
                    let codec = auto<Weather> ()

                    let record = {
                        AirTemperature = 22.5
                        WindSpeed = 3.0
                    }

                    let json = codec.encodeWith CaseRules.SnakeCase record
                    let map = parseRaw json
                    let temp = getFloat backend map "air_temperature"
                    assertThat temp (isEqualTo 22.5)
            )
            test (
                "auto encode with camelCase",
                fun _ ->
                    let codec = auto<Weather> ()

                    let record = {
                        AirTemperature = 22.5
                        WindSpeed = 3.0
                    }

                    let json = codec.encodeWith CaseRules.LowerFirst record
                    let map = parseRaw json
                    let temp = getFloat backend map "airTemperature"
                    assertThat temp (isEqualTo 22.5)
            )
            test (
                "same codec different casing",
                fun _ ->
                    let codec = auto<Weather> ()

                    let record = {
                        AirTemperature = 22.5
                        WindSpeed = 3.0
                    }

                    // Encode as snake_case
                    let snakeJson = codec.encodeWith CaseRules.SnakeCase record
                    let snakeMap = parseRaw snakeJson

                    assertThat (backend.ContainsKey(snakeMap, "air_temperature")) isTrue

                    // Encode same record as camelCase
                    let camelJson = codec.encodeWith CaseRules.LowerFirst record
                    let camelMap = parseRaw camelJson

                    assertThat (backend.ContainsKey(camelMap, "airTemperature")) isTrue
            )
        ]
    )

// ============================================================================
// Default CaseRules + withCaseRules — the configured-once flow
// ============================================================================

let private defaultAndWithCaseRulesTests =
    testList (
        "Default CaseRules + withCaseRules — the configured-once flow",
        [
            test (
                "default case rule is LowerFirst",
                fun _ ->
                    let codec = auto<Weather> ()
                    assertThat codec.caseRules (isEqualTo CaseRules.LowerFirst)
            )
            test (
                "default codec encodes and decodes camelCase",
                fun _ ->
                    let codec = auto<Weather> ()

                    let record = {
                        AirTemperature = 22.5
                        WindSpeed = 3.0
                    }
                    // No CaseRules argument — uses the codec's default (LowerFirst).
                    let json = codec.encode record
                    let map = parseRaw json

                    assertThat (backend.ContainsKey(map, "airTemperature")) isTrue

                    assertThat (backend.ContainsKey(map, "windSpeed")) isTrue

                    match codec.decode map with
                    | Ok w ->
                        assertThat w.AirTemperature (isEqualTo 22.5)
                        assertThat w.WindSpeed (isEqualTo 3.0)
                    | Error e -> assertThat (sprintf "Error: %A" e) (isEqualTo "Ok")
            )
            test (
                "withCaseRules switches default for round-trip",
                fun _ ->
                    let codec =
                        auto<Weather> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let record = {
                        AirTemperature = 22.5
                        WindSpeed = 3.0
                    }

                    let json = codec.encode record
                    let map = parseRaw json

                    assertThat (backend.ContainsKey(map, "air_temperature")) isTrue

                    match codec.decode map with
                    | Ok w -> assertThat w.AirTemperature (isEqualTo 22.5)
                    | Error e -> assertThat (sprintf "Error: %A" e) (isEqualTo "Ok")
            )
            test (
                "withCaseRules survives withModel composition",
                fun _ ->
                    let codec =
                        auto<Weather> ()
                        |> withModel (fun w -> if w.WindSpeed >= 0.0 then Ok w else Error [])
                        |> withCaseRules CaseRules.SnakeCase

                    assertThat codec.caseRules (isEqualTo CaseRules.SnakeCase)

                    let record = {
                        AirTemperature = 22.5
                        WindSpeed = 3.0
                    }

                    let json = codec.encode record
                    let map = parseRaw json

                    assertThat (backend.ContainsKey(map, "air_temperature")) isTrue
            )
        ]
    )

let tests =
    testList ("CaseRules", [ applyCaseRuleTests; autoCodecCaseRulesTests; defaultAndWithCaseRulesTests ])
