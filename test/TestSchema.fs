(**
# TestSchema — Tests for Schema module

Tests for format-agnostic validation: coercion, validateMap (string maps),
validateJson (BEAM maps), JsonValue erased DU, and error handling.
*)

module Fable.TypedJson.Tests.Schema

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


// ============================================================================
// Test Record Types
// ============================================================================

type SimpleInput = { Name: string; Age: int }

type FloatInput = { Temperature: float; Humidity: float }

(**
adr: multi-word fields kept as their own type — every other record here is single-word, where `lowerFirstTransform` is a no-op and cannot catch key drift
*)
type MultiWordInput = {
    AirTemperature: float
    RelativeHumidity: float
}

type BoolInput = { Active: bool; Debug: bool }

type OptionalInput = { Name: string; Email: string option }

type MixedInput = {
    Label: string
    Count: int
    Score: float
    Enabled: bool
}

// ============================================================================
// validateMap — String Map (ToolCall.input simulation)
// ============================================================================

let private validateMapTests =
    testList (
        "validateMap — String Map (ToolCall.input simulation)",
        [
            test (
                "validateMap simple strings",
                fun _ ->
                    let input = Map.ofList [ "name", "Alice"; "age", "30" ]

                    match validateMap<SimpleInput> input with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "Alice")
                        assertThat r.Age (isEqualTo 30)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateMap float from string",
                fun _ ->
                    let input = Map.ofList [ "temperature", "22.5"; "humidity", "65.0" ]

                    match validateMap<FloatInput> input with
                    | Ok r ->
                        assertThat r.Temperature (isEqualTo 22.5)
                        assertThat r.Humidity (isEqualTo 65.0)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateMap bool from string",
                fun _ ->
                    let input = Map.ofList [ "active", "true"; "debug", "false" ]

                    match validateMap<BoolInput> input with
                    | Ok r ->
                        assertThat r.Active isTrue
                        assertThat r.Debug isFalse
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateMap missing required field",
                fun _ ->
                    let input = Map.ofList [ "name", "Alice" ]

                    match validateMap<SimpleInput> input with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "age")
            )
            test (
                "validateMap all fields missing",
                fun _ ->
                    let input: Map<string, string> = Map.empty

                    match validateMap<SimpleInput> input with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors -> assertThat errors.Length (isEqualTo 2)
            )
            test (
                "validateMap optional present",
                fun _ ->
                    let input = Map.ofList [ "name", "Alice"; "email", "a@b.com" ]

                    match validateMap<OptionalInput> input with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "Alice")
                        assertThat r.Email (isEqualTo (Some "a@b.com"))
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateMap optional missing",
                fun _ ->
                    let input = Map.ofList [ "name", "Alice" ]

                    match validateMap<OptionalInput> input with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "Alice")
                        assertThat r.Email (isEqualTo None)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateMap invalid int string",
                fun _ ->
                    let input = Map.ofList [ "name", "Alice"; "age", "not_a_number" ]

                    match validateMap<SimpleInput> input with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "age")

                        assertThat (errors.[0].message.Contains("cannot parse")) isTrue
            )
            test (
                "validateMap invalid float string",
                fun _ ->
                    let input = Map.ofList [ "temperature", "hot"; "humidity", "65.0" ]

                    match validateMap<FloatInput> input with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "temperature")
            )
            test (
                "validateMap invalid bool string",
                fun _ ->
                    let input = Map.ofList [ "active", "yes"; "debug", "false" ]

                    match validateMap<BoolInput> input with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "active")
            )
            test (
                "validateMap mixed types all from strings",
                fun _ ->
                    let input =
                        Map.ofList [ "label", "test"; "count", "5"; "score", "9.5"; "enabled", "true" ]

                    match validateMap<MixedInput> input with
                    | Ok r ->
                        assertThat r.Label (isEqualTo "test")
                        assertThat r.Count (isEqualTo 5)
                        assertThat r.Score (isEqualTo 9.5)
                        assertThat r.Enabled isTrue
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateMap accumulates multiple errors",
                fun _ ->
                    let input = Map.ofList [ "label", "test" ]

                    match validateMap<MixedInput> input with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        // count, score, enabled are all missing
                        assertThat errors.Length (isEqualTo 3)
            )
        ]
    )

// ============================================================================
// validateJson — BeamMap (parsed JSON)
// ============================================================================

let private validateJsonTests =
    testList (
        "validateJson — BeamMap (parsed JSON)",
        [
            test (
                "validateJson with native types",
                fun _ ->
                    let map = parseRaw """{"name":"Alice","age":30}"""

                    match validateJson<SimpleInput> map with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "Alice")
                        assertThat r.Age (isEqualTo 30)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateJson with floats",
                fun _ ->
                    let map = parseRaw """{"temperature":22.5,"humidity":65.0}"""

                    match validateJson<FloatInput> map with
                    | Ok r ->
                        assertThat r.Temperature (isEqualTo 22.5)
                        assertThat r.Humidity (isEqualTo 65.0)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateJson with bools",
                fun _ ->
                    let map = parseRaw """{"active":true,"debug":false}"""

                    match validateJson<BoolInput> map with
                    | Ok r ->
                        assertThat r.Active isTrue
                        assertThat r.Debug isFalse
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateJson missing field",
                fun _ ->
                    let map = parseRaw """{"name":"Alice"}"""

                    match validateJson<SimpleInput> map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors ->
                        assertThat errors.Length (isEqualTo 1)
                        assertThat (errors.[0].path) (isEqualTo "age")
            )
            test (
                "validateJson optional present",
                fun _ ->
                    let map = parseRaw """{"name":"Alice","email":"a@b.com"}"""

                    match validateJson<OptionalInput> map with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "Alice")
                        assertThat r.Email (isEqualTo (Some "a@b.com"))
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "validateJson optional missing",
                fun _ ->
                    let map = parseRaw """{"name":"Alice"}"""

                    match validateJson<OptionalInput> map with
                    | Ok r ->
                        assertThat r.Name (isEqualTo "Alice")
                        assertThat r.Email (isEqualTo None)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// Coercion Tests
// ============================================================================

(**
Cross-type coercion, exercised through the public entry points rather than the
walker. `validateMap` feeds every value as a string — the LLM-tool-call shape,
and the reason cross-type coercion exists at all — while `validateJson` feeds
backend-native JSON values, so between them they cover both directions a
primitive node can be reached from.

These used to call `Schema.coerce` directly, which dispatched on
`targetType.FullName` per value. That function is gone and the plan that
replaced it is internal, so the tests go through the API a consumer has.

invariant: a string field value coerces to the record's declared primitive type
*)
type IntBox = { Value: int }

type FloatBox = { Value: float }

type BoolBox = { Value: bool }

type StringBox = { Value: string }

let private coercionTests =
    testList (
        "Coercion",
        [
            test (
                "coercion string to int",
                fun _ ->
                    match validateMap<IntBox> (Map.ofList [ "value", "42" ]) with
                    | Ok r -> assertThat r.Value (isEqualTo 42)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "coercion string to float",
                fun _ ->
                    match validateMap<FloatBox> (Map.ofList [ "value", "3.14" ]) with
                    | Ok r -> assertThat r.Value (isEqualTo 3.14)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "coercion string to bool true",
                fun _ ->
                    match validateMap<BoolBox> (Map.ofList [ "value", "true" ]) with
                    | Ok r -> assertThat r.Value isTrue
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "coercion string to bool false",
                fun _ ->
                    match validateMap<BoolBox> (Map.ofList [ "value", "false" ]) with
                    | Ok r -> assertThat r.Value isFalse
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "coercion int to float",
                fun _ ->
                    match validateJson<FloatBox> (parseRaw """{"value":42}""") with
                    | Ok r -> assertThat r.Value (isEqualTo 42.0)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "coercion float to int",
                fun _ ->
                    match validateJson<IntBox> (parseRaw """{"value":42.9}""") with
                    | Ok r -> assertThat r.Value (isEqualTo 42)
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "coercion int to string",
                fun _ ->
                    match validateJson<StringBox> (parseRaw """{"value":42}""") with
                    | Ok r -> assertThat r.Value (isEqualTo "42")
                    | Error e -> assertThat (formatErrors e) (isEqualTo "Ok")
            )
            test (
                "coercion invalid string to int",
                fun _ ->
                    match validateMap<IntBox> (Map.ofList [ "value", "not_a_number" ]) with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors -> assertThat ((formatErrors errors).Contains "cannot parse") isTrue
            )
            test (
                "coercion invalid string to float",
                fun _ ->
                    match validateMap<FloatBox> (Map.ofList [ "value", "not_a_number" ]) with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors -> assertThat ((formatErrors errors).Contains "cannot parse") isTrue
            )
            test (
                "coercion invalid string to bool",
                fun _ ->
                    match validateMap<BoolBox> (Map.ofList [ "value", "maybe" ]) with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors -> assertThat ((formatErrors errors).Contains "cannot parse") isTrue
            )
            test (
                "a non-coercible value reports the target type",
                fun _ ->
                    match validateJson<IntBox> (parseRaw """{"value":{"nested":1}}""") with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errors -> assertThat ((formatErrors errors).Contains "System.Int32") isTrue
            )
        ]
    )

// ============================================================================
// formatErrors
// ============================================================================

let private formatErrorsTests =
    testList (
        "formatErrors",
        [
            test (
                "formatErrors produces readable string",
                fun _ ->
                    let errors = [
                        {
                            path = "name"
                            message = "missing field (expected String)"
                        }
                        {
                            path = "age"
                            message = "cannot parse 'xyz' as int"
                        }
                    ]

                    let result = formatErrors errors
                    assertThat (result.Contains("name:")) isTrue
                    assertThat (result.Contains("age:")) isTrue
            )
        ]
    )

// ============================================================================
// dump — Record to BeamMap
// ============================================================================

type Street = { Street: string; City: string }

type Outer = { Label: string; Inner: Street }

let private dumpTests =
    testList (
        "dump — Record to BeamMap",
        [
            test (
                "dump simple record",
                fun _ ->
                    let record = { Name = "Alice"; Age = 30 }
                    let map = dump record
                    let name = getString backend map "name"
                    let age = getInt backend map "age"
                    assertThat name (isEqualTo "Alice")
                    assertThat age (isEqualTo 30)
            )
            test (
                "dump record with option some",
                fun _ ->
                    let record = {
                        Name = "Alice"
                        Email = Some "a@b.com"
                    }

                    let map = dump record
                    let name = getString backend map "name"
                    let email = getString backend map "email"
                    assertThat name (isEqualTo "Alice")
                    assertThat email (isEqualTo "a@b.com")
            )
            test (
                "dump record with option none omits field",
                fun _ ->
                    let record = { Name = "Alice"; Email = None }
                    let map = dump record
                    let hasEmail = backend.ContainsKey(map, "email")
                    assertThat hasEmail isFalse
            )
            (**
            `dump` walks only the top level: a record-typed field is `Put` into
            the map verbatim, so whatever the backend's `Stringify` makes of a
            raw F# record leaks into the output. The stated invariant is that a
            dumped map validates back — this pins whether that actually holds
            one level down.

            invariant: `dump` and `validateJson` agree on nested keys, so a nested record round-trips
            *)
            test (
                "dump round-trips a nested record through validateJson",
                fun _ ->
                    let original = {
                        Label = "outer"
                        Inner = {
                            Street.Street = "Main 1"
                            City = "Oslo"
                        }
                    }

                    match validateJson<Outer> (dump original) with
                    | Ok decoded ->
                        assertThat decoded.Label (isEqualTo "outer")
                        assertThat decoded.Inner.Street (isEqualTo "Main 1")
                        assertThat decoded.Inner.City (isEqualTo "Oslo")
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// Multi-word field keys
// ============================================================================

(**
`dump` / `validateJson` / `validateString` bypass `CaseRules` and key off
`lowerFirstTransform` directly. Every other record in this file is single-word,
where that transform is a no-op — so nothing here pinned the multi-word key, the
one place the backends could disagree. BEAM emitted `air_temperature` while
Python, JS and .NET emitted `airTemperature` until `PropertyInfo.Name` started
reporting the F# field name on BEAM (fable-compiler/Fable#4766, Fable 5.8.1).

invariant: `dump` and `validateJson` agree on the key, so a dumped map round-trips back through validate
invariant: the key is camelCase (`airTemperature`) on all four targets — never snake_case, never PascalCase
*)
let private multiWordKeyTests =
    testList (
        "Multi-word field keys",
        [
            test (
                "dump emits camelCase keys",
                fun _ ->
                    let map =
                        dump {
                            AirTemperature = 22.5
                            RelativeHumidity = 65.0
                        }

                    assertThat (getFloat backend map "airTemperature") (isEqualTo 22.5)
                    assertThat (getFloat backend map "relativeHumidity") (isEqualTo 65.0)

                    // Guard the two spellings the backends used to drift to.
                    assertThat (backend.ContainsKey(map, "air_temperature")) isFalse
                    assertThat (backend.ContainsKey(map, "AirTemperature")) isFalse
            )
            test (
                "validateJson reads camelCase keys",
                fun _ ->
                    let map = parseRaw """{"airTemperature":22.5,"relativeHumidity":65.0}"""

                    match validateJson<MultiWordInput> map with
                    | Ok record ->
                        assertThat record.AirTemperature (isEqualTo 22.5)
                        assertThat record.RelativeHumidity (isEqualTo 65.0)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
            test (
                "validateMap reads camelCase keys",
                fun _ ->
                    let map = Map.ofList [ "airTemperature", "22.5"; "relativeHumidity", "65.0" ]

                    match validateMap<MultiWordInput> map with
                    | Ok record ->
                        assertThat record.AirTemperature (isEqualTo 22.5)
                        assertThat record.RelativeHumidity (isEqualTo 65.0)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
            test (
                "dump round-trips through validateJson",
                fun _ ->
                    let original = {
                        AirTemperature = -3.5
                        RelativeHumidity = 91.0
                    }

                    match validateJson<MultiWordInput> (dump original) with
                    | Ok decoded ->
                        assertThat decoded.AirTemperature (isEqualTo original.AirTemperature)
                        assertThat decoded.RelativeHumidity (isEqualTo original.RelativeHumidity)
                    | Error errors -> assertThat (sprintf "Error: %A" errors) (isEqualTo "Ok")
            )
        ]
    )

let tests =
    testList (
        "Schema",
        [
            validateMapTests
            validateJsonTests
            coercionTests
            formatErrorsTests
            dumpTests
            multiWordKeyTests
        ]
    )
