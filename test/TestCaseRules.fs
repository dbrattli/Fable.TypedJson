(**
# TestCaseRules — Tests for case rule transformation

Tests that applyCaseRule correctly transforms field names
and that auto codec uses case rules for JSON key mapping.
*)

module Fable.TypedJson.Tests.CaseRules

open Fable.TypedJson.Testing
open Fable.TypedJson.Json

#if PYTHON
open Fable.TypedJson.Python.Json

let backend = python
#else
#if JS
open Fable.TypedJson.JS.Json

let backend = js
#else
open Fable.TypedJson.Beam.Json

let backend = beam
#endif
#endif


// ============================================================================
// applyCaseRule Tests
// ============================================================================

[<Fact>]
let ``test snake_case conversion`` () =
    applyCaseRule CaseRules.SnakeCase "AirTemperature"
    |> equal "air_temperature"

[<Fact>]
let ``test snake_case multi-word`` () =
    applyCaseRule CaseRules.SnakeCase "RelativeHumidity"
    |> equal "relative_humidity"

[<Fact>]
let ``test snake_case single word`` () =
    applyCaseRule CaseRules.SnakeCase "Name"
    |> equal "name"

[<Fact>]
let ``test snake_case already lowercase`` () =
    applyCaseRule CaseRules.SnakeCase "name"
    |> equal "name"

[<Fact>]
let ``test lower_first conversion`` () =
    applyCaseRule CaseRules.LowerFirst "AirTemperature"
    |> equal "airTemperature"

[<Fact>]
let ``test lower_first single word`` () =
    applyCaseRule CaseRules.LowerFirst "Name"
    |> equal "name"

[<Fact>]
let ``test kebab_case conversion`` () =
    applyCaseRule CaseRules.KebabCase "AirTemperature"
    |> equal "air-temperature"

[<Fact>]
let ``test snake_case_all_caps conversion`` () =
    applyCaseRule CaseRules.SnakeCaseAllCaps "AirTemperature"
    |> equal "AIR_TEMPERATURE"

[<Fact>]
let ``test none preserves case`` () =
    applyCaseRule CaseRules.None "AirTemperature"
    |> equal "AirTemperature"

[<Fact>]
let ``test pascal_case conversion`` () =
    applyCaseRule CaseRules.PascalCase "air_temperature"
    |> equal "AirTemperature"

[<Fact>]
let ``test pascal_case single word`` () =
    applyCaseRule CaseRules.PascalCase "name"
    |> equal "Name"

// ============================================================================
// Auto Codec with Different Case Rules
// ============================================================================

type Weather = {
    AirTemperature: float
    WindSpeed: float
}

[<Fact>]
let ``test auto decode with snake_case keys`` () =
    let codec = auto<Weather> ()
    let map = parseRaw """{"air_temperature":22.5,"wind_speed":3.0}"""

    match codec.decodeWith CaseRules.SnakeCase map with
    | Ok w ->
        w.AirTemperature |> equal 22.5
        w.WindSpeed |> equal 3.0
    | Error e -> equal "Ok" (sprintf "Error: %A" e)

[<Fact>]
let ``test auto decode with camelCase keys`` () =
    let codec = auto<Weather> ()
    let map = parseRaw """{"airTemperature":22.5,"windSpeed":3.0}"""

    match codec.decodeWith CaseRules.LowerFirst map with
    | Ok w ->
        w.AirTemperature |> equal 22.5
        w.WindSpeed |> equal 3.0
    | Error e -> equal "Ok" (sprintf "Error: %A" e)

[<Fact>]
let ``test auto decode with PascalCase keys`` () =
    let codec = auto<Weather> ()
    let map = parseRaw """{"AirTemperature":22.5,"WindSpeed":3.0}"""

    match codec.decodeWith CaseRules.PascalCase map with
    | Ok w ->
        w.AirTemperature |> equal 22.5
        w.WindSpeed |> equal 3.0
    | Error e -> equal "Ok" (sprintf "Error: %A" e)

[<Fact>]
let ``test auto encode with snake_case`` () =
    let codec = auto<Weather> ()

    let record = {
        AirTemperature = 22.5
        WindSpeed = 3.0
    }

    let json = codec.encodeWith CaseRules.SnakeCase record
    let map = parseRaw json
    let temp = unbox<float> (backend.Get(map, "air_temperature"))
    temp |> equal 22.5

[<Fact>]
let ``test auto encode with camelCase`` () =
    let codec = auto<Weather> ()

    let record = {
        AirTemperature = 22.5
        WindSpeed = 3.0
    }

    let json = codec.encodeWith CaseRules.LowerFirst record
    let map = parseRaw json
    let temp = unbox<float> (backend.Get(map, "airTemperature"))
    temp |> equal 22.5

[<Fact>]
let ``test same codec different casing`` () =
    let codec = auto<Weather> ()

    let record = {
        AirTemperature = 22.5
        WindSpeed = 3.0
    }

    // Encode as snake_case
    let snakeJson = codec.encodeWith CaseRules.SnakeCase record
    let snakeMap = parseRaw snakeJson

    backend.ContainsKey(snakeMap, "air_temperature")
    |> equal true

    // Encode same record as camelCase
    let camelJson = codec.encodeWith CaseRules.LowerFirst record
    let camelMap = parseRaw camelJson

    backend.ContainsKey(camelMap, "airTemperature")
    |> equal true

// ============================================================================
// Default CaseRules + withCaseRules — the configured-once flow
// ============================================================================

[<Fact>]
let ``test default case rule is LowerFirst`` () =
    let codec = auto<Weather> ()
    codec.caseRules |> equal CaseRules.LowerFirst

[<Fact>]
let ``test default codec encodes and decodes camelCase`` () =
    let codec = auto<Weather> ()

    let record = {
        AirTemperature = 22.5
        WindSpeed = 3.0
    }
    // No CaseRules argument — uses the codec's default (LowerFirst).
    let json = codec.encode record
    let map = parseRaw json

    backend.ContainsKey(map, "airTemperature")
    |> equal true

    backend.ContainsKey(map, "windSpeed")
    |> equal true

    match codec.decode map with
    | Ok w ->
        w.AirTemperature |> equal 22.5
        w.WindSpeed |> equal 3.0
    | Error e -> equal "Ok" (sprintf "Error: %A" e)

[<Fact>]
let ``test withCaseRules switches default for round-trip`` () =
    let codec =
        auto<Weather> ()
        |> withCaseRules CaseRules.SnakeCase

    let record = {
        AirTemperature = 22.5
        WindSpeed = 3.0
    }

    let json = codec.encode record
    let map = parseRaw json

    backend.ContainsKey(map, "air_temperature")
    |> equal true

    match codec.decode map with
    | Ok w -> w.AirTemperature |> equal 22.5
    | Error e -> equal "Ok" (sprintf "Error: %A" e)

[<Fact>]
let ``test withCaseRules survives withModel composition`` () =
    let codec =
        auto<Weather> ()
        |> withModel (fun w -> if w.WindSpeed >= 0.0 then Ok w else Error [])
        |> withCaseRules CaseRules.SnakeCase

    codec.caseRules |> equal CaseRules.SnakeCase

    let record = {
        AirTemperature = 22.5
        WindSpeed = 3.0
    }

    let json = codec.encode record
    let map = parseRaw json

    backend.ContainsKey(map, "air_temperature")
    |> equal true
