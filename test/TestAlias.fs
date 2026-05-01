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

#if PYTHON
open Fable.TypedJson.Python.Json
let backend = python
#else
open Fable.TypedJson.Beam.Json
let backend = beam
#endif

type WeatherRequest = { Location: string; Days: int }

[<Fact>]
let ``test alias redirects decode lookup`` () =
    let codec =
        auto<WeatherRequest> ()
        |> alias "Location" "loc"
        |> alias "Days" "n"

    // JSON uses the alias keys, not snake_case derived from field names.
    let map = parseRaw """{"loc":"Oslo","n":3}"""

    match codec.decode CaseRules.SnakeCase map with
    | Ok r ->
        r.Location |> equal "Oslo"
        r.Days |> equal 3
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test alias affects error path on missing key`` () =
    let codec = auto<WeatherRequest> () |> alias "Location" "loc"
    // JSON is missing the aliased key.
    let map = parseRaw """{"days":3}"""

    match codec.decode CaseRules.SnakeCase map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        // The schema looked up "loc" and missed it. The FieldError's `path`
        // is the field name as reflection sees it (BEAM lowercases, Python
        // preserves), so we check case-insensitively.
        errs.Length |> equal 1
        let path = errs.[0].path.ToLower()
        path |> equal "location"

[<Fact>]
let ``test alias redirects encode output`` () =
    let codec = auto<WeatherRequest> () |> alias "Location" "loc"
    let json = codec.encode CaseRules.SnakeCase { Location = "Oslo"; Days = 5 }
    let parsed = parseRaw json

    backend.ContainsKey(parsed, "loc") |> equal true
    backend.ContainsKey(parsed, "location") |> equal false
    unbox<string> (backend.Get(parsed, "loc")) |> equal "Oslo"

[<Fact>]
let ``test alias falls through to case rule for unaliased fields`` () =
    let codec = auto<WeatherRequest> () |> alias "Location" "loc"
    let json = codec.encode CaseRules.SnakeCase { Location = "Oslo"; Days = 5 }
    let parsed = parseRaw json

    // "Days" wasn't aliased, so it follows the SnakeCase rule.
    backend.ContainsKey(parsed, "days") |> equal true
    unbox<int> (backend.Get(parsed, "days")) |> equal 5

[<Fact>]
let ``test alias propagates to JSON schema property keys`` () =
    let codec =
        auto<WeatherRequest> ()
        |> alias "Location" "loc"
        |> alias "Days" "n"

    let json = jsonSchemaOfCodec emptyRegistry CaseRules.SnakeCase codec
    let parsed = parseRaw json

    let props = backend.Get(parsed, "properties")
    backend.ContainsKey(props, "loc") |> equal true
    backend.ContainsKey(props, "n") |> equal true
    backend.ContainsKey(props, "location") |> equal false
    backend.ContainsKey(props, "days") |> equal false

    // `required` must list the aliased keys too.
    let required = backend.Get(parsed, "required")

    let toListOfStrings (arr: obj) =
        let len = backend.ArrayLength arr

        [ for i in 0 .. len - 1 -> unbox<string> (backend.ArrayAt(arr, i)) ]
        |> List.sort

    toListOfStrings required |> equal [ "loc"; "n" ]

[<Fact>]
let ``test alias preserves withModel composition`` () =
    // Combinator order matters — verify alias works after withModel.
    let codec =
        auto<WeatherRequest> ()
        |> withModel (fun r ->
            if r.Days > 0 then
                Ok r
            else
                Error [ { path = ""; message = "days must be positive" } ])
        |> alias "Location" "loc"

    let map = parseRaw """{"loc":"Oslo","days":3}"""

    match codec.decode CaseRules.SnakeCase map with
    | Ok r ->
        r.Location |> equal "Oslo"
        r.Days |> equal 3
    | Error errs -> equal "Ok" (formatErrors errs)

    // Negative path: withModel still fires even when alias was applied later.
    let bad = parseRaw """{"loc":"Oslo","days":-1}"""

    match codec.decode CaseRules.SnakeCase bad with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs
        formatted.Contains("must be positive") |> equal true
