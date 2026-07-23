(**
# TestAlias — Phase 2.9: per-field JSON-key overrides

Verifies `TypedJson.alias` redirects:
1. decode lookup (consumes JSON with the alias key, not the F# field name)
2. encode output (emits the alias key, not the case-rule-derived name)
3. JSON Schema generation (`properties` and `required` use the alias key)
*)

module Fable.TypedJson.Tests.Alias

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

type WeatherRequest = { Location: string; Days: int }

let tests =
    testList (
        "Alias",
        [
            test (
                "alias redirects decode lookup",
                fun _ ->
                    let codec =
                        auto<WeatherRequest> ()
                        |> withCaseRules CaseRules.SnakeCase
                        |> alias "Location" "loc"
                        |> alias "Days" "n"

                    // JSON uses the alias keys, not snake_case derived from field names.
                    let map = parseRaw """{"loc":"Oslo","n":3}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Location (isEqualTo "Oslo")
                        assertThat r.Days (isEqualTo 3)
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "alias affects error path on missing key",
                fun _ ->
                    let codec =
                        auto<WeatherRequest> ()
                        |> withCaseRules CaseRules.SnakeCase
                        |> alias "Location" "loc"
                    // JSON is missing the aliased key.
                    let map = parseRaw """{"days":3}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        // The error path uses the resolved JSON key — alias if defined,
                        // otherwise the case-rule-derived form. Missing "loc" produces
                        // path "loc", not the F# field name "Location".
                        assertThat errs.Length (isEqualTo 1)
                        assertThat (errs.[0].path) (isEqualTo "loc")
            )
            test (
                "alias redirects encode output",
                fun _ ->
                    let codec =
                        auto<WeatherRequest> ()
                        |> withCaseRules CaseRules.SnakeCase
                        |> alias "Location" "loc"

                    let json = codec.encode { Location = "Oslo"; Days = 5 }
                    let parsed = parseRaw json

                    assertThat (backend.ContainsKey(parsed, "loc")) isTrue

                    assertThat (backend.ContainsKey(parsed, "location")) isFalse

                    assertThat (getString backend parsed "loc") (isEqualTo "Oslo")
            )
            test (
                "alias falls through to case rule for unaliased fields",
                fun _ ->
                    let codec =
                        auto<WeatherRequest> ()
                        |> withCaseRules CaseRules.SnakeCase
                        |> alias "Location" "loc"

                    let json = codec.encode { Location = "Oslo"; Days = 5 }
                    let parsed = parseRaw json

                    // "Days" wasn't aliased, so it follows the codec's SnakeCase rule.
                    assertThat (backend.ContainsKey(parsed, "days")) isTrue

                    assertThat (getInt backend parsed "days") (isEqualTo 5)
            )
            test (
                "alias propagates to JSON schema property keys",
                fun _ ->
                    let codec =
                        auto<WeatherRequest> ()
                        |> withCaseRules CaseRules.SnakeCase
                        |> alias "Location" "loc"
                        |> alias "Days" "n"

                    let json = jsonSchemaOfCodec emptyRegistry codec
                    let parsed = parseRaw json

                    let props = backend.Get(parsed, "properties")
                    assertThat (backend.ContainsKey(props, "loc")) isTrue
                    assertThat (backend.ContainsKey(props, "n")) isTrue

                    assertThat (backend.ContainsKey(props, "location")) isFalse

                    assertThat (backend.ContainsKey(props, "days")) isFalse

                    // `required` must list the aliased keys too.
                    let required = backend.Get(parsed, "required")

                    let toListOfStrings (arr: obj) =
                        let len = backend.ArrayLength arr

                        [ for i in 0 .. len - 1 -> arrayAtString backend arr i ]
                        |> List.sort

                    assertThat (toListOfStrings required) (isEqualTo [ "loc"; "n" ])
            )
            test (
                "alias preserves withModel composition",
                fun _ ->
                    // Combinator order matters — verify alias works after withModel.
                    let codec =
                        auto<WeatherRequest> ()
                        |> withCaseRules CaseRules.SnakeCase
                        |> withModel (fun r ->
                            if r.Days > 0 then
                                Ok r
                            else
                                Error [
                                    {
                                        path = ""
                                        message = "days must be positive"
                                    }
                                ])
                        |> alias "Location" "loc"

                    let map = parseRaw """{"loc":"Oslo","days":3}"""

                    match codec.decode map with
                    | Ok r ->
                        assertThat r.Location (isEqualTo "Oslo")
                        assertThat r.Days (isEqualTo 3)
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")

                    // Negative path: withModel still fires even when alias was applied later.
                    let bad = parseRaw """{"loc":"Oslo","days":-1}"""

                    match codec.decode bad with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs

                        assertThat (formatted.Contains("must be positive")) isTrue
            )
        ]
    )
