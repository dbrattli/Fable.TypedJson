(**
# TestUnion — Phase 3.12: tagged discriminated unions

Verifies that `auto<'T>` handles discriminated union types as record fields
and as the top-level type, with a `"type"` discriminator key (default).

For each case:
- The case name is transformed by the codec's `caseRules` to produce the
  discriminator value (e.g., `Search` → `"search"` under `LowerFirst`).
- A single-record-field case "flattens" its payload fields into the same
  JSON object as the discriminator (Pydantic-style).
- A fieldless case emits/accepts just `{"type": "case_name"}`.
*)

module Fable.TypedJson.Tests.Union

open Fable.TypedJson.Testing
open Fable.TypedJson.Schema
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
// Tagged DU as the top-level type
// ============================================================================

type SearchInput = { Query: string; MaxResults: int }
type CalcInput = { Expression: string }

type Tool =
    | Search of SearchInput
    | Calculate of CalcInput
    | Ping

[<Fact>]
let ``test decode tagged union top-level — search case`` () =
    let codec = auto<Tool> ()
    let map = parseRaw """{"type":"search","query":"hello","maxResults":5}"""

    match codec.decode map with
    | Ok(Search input) ->
        input.Query |> equal "hello"
        input.MaxResults |> equal 5
    | Ok other -> equal "Search" (sprintf "%A" other)
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test decode tagged union top-level — calculate case`` () =
    let codec = auto<Tool> ()
    let map = parseRaw """{"type":"calculate","expression":"1+1"}"""

    match codec.decode map with
    | Ok(Calculate input) -> input.Expression |> equal "1+1"
    | Ok other -> equal "Calculate" (sprintf "%A" other)
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test decode tagged union — fieldless case`` () =
    let codec = auto<Tool> ()
    let map = parseRaw """{"type":"ping"}"""

    match codec.decode map with
    | Ok Ping -> ()
    | Ok other -> equal "Ping" (sprintf "%A" other)
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test decode tagged union — unknown discriminator value`` () =
    let codec = auto<Tool> ()
    let map = parseRaw """{"type":"bogus","query":"x"}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs
        formatted.Contains("bogus") |> equal true

[<Fact>]
let ``test decode tagged union — missing discriminator`` () =
    let codec = auto<Tool> ()
    let map = parseRaw """{"query":"x"}"""

    match codec.decode map with
    | Ok _ -> equal "Error" "Ok"
    | Error errs ->
        let formatted = formatErrors errs
        formatted.Contains("type") |> equal true

[<Fact>]
let ``test encode tagged union — search case`` () =
    let codec = auto<Tool> ()
    let value = Search { Query = "hello"; MaxResults = 5 }
    let json = codec.encode value
    let parsed = parseRaw json

    unbox<string> (backend.Get(parsed, "type"))
    |> equal "search"

    unbox<string> (backend.Get(parsed, "query"))
    |> equal "hello"

    unbox<int> (backend.Get(parsed, "maxResults"))
    |> equal 5

[<Fact>]
let ``test encode tagged union — fieldless case`` () =
    let codec = auto<Tool> ()
    let json = codec.encode Ping
    let parsed = parseRaw json

    unbox<string> (backend.Get(parsed, "type"))
    |> equal "ping"

[<Fact>]
let ``test round-trip tagged union — every case`` () =
    let codec = auto<Tool> ()

    let cases = [
        Search { Query = "q"; MaxResults = 3 }
        Calculate { Expression = "2*3" }
        Ping
    ]

    for original in cases do
        let json = codec.encode original

        match codec.decode (parseRaw json) with
        | Ok roundtripped -> roundtripped |> equal original
        | Error errs -> equal "Ok" (formatErrors errs)

// ============================================================================
// Tagged DU as a record field
// ============================================================================

type Envelope = { Id: int; Tool: Tool }

[<Fact>]
let ``test tagged union as record field — round-trip`` () =
    let codec = auto<Envelope> ()

    let original = {
        Id = 42
        Tool = Search { Query = "fsharp"; MaxResults = 10 }
    }

    let json = codec.encode original

    match codec.decode (parseRaw json) with
    | Ok roundtripped -> roundtripped |> equal original
    | Error errs -> equal "Ok" (formatErrors errs)
