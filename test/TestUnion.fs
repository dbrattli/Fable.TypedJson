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

    getString backend parsed "type" |> equal "search"
    getString backend parsed "query" |> equal "hello"
    getInt backend parsed "maxResults" |> equal 5

[<Fact>]
let ``test encode tagged union — fieldless case`` () =
    let codec = auto<Tool> ()
    let json = codec.encode Ping
    let parsed = parseRaw json

    getString backend parsed "type" |> equal "ping"

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

// ============================================================================
// Snake-case discriminator tags (Anthropic content-block style)
//
// Multi-word PascalCase case names get transformed through the codec's
// CaseRules — so `ToolUse` under `SnakeCase` becomes the `"tool_use"` tag.
// Mirrors how Anthropic / OpenAI / MCP wire formats encode message-type
// discriminators.
// ============================================================================

type TextPayload = { Text: string }
type ToolUsePayload = { Id: string; Name: string }

type ContentBlock =
    | Text of TextPayload
    | ToolUse of ToolUsePayload
    | ToolResult

[<Fact>]
let ``test snake_case union tag — encode multi-word case`` () =
    let codec =
        auto<ContentBlock> ()
        |> withCaseRules CaseRules.SnakeCase

    let value = ToolUse { Id = "abc"; Name = "search" }
    let json = codec.encode value
    let parsed = parseRaw json

    getString backend parsed "type"
    |> equal "tool_use"

    getString backend parsed "id" |> equal "abc"
    getString backend parsed "name" |> equal "search"

[<Fact>]
let ``test snake_case union tag — decode multi-word case`` () =
    let codec =
        auto<ContentBlock> ()
        |> withCaseRules CaseRules.SnakeCase

    let map = parseRaw """{"type":"tool_use","id":"xyz","name":"calc"}"""

    match codec.decode map with
    | Ok(ToolUse p) ->
        p.Id |> equal "xyz"
        p.Name |> equal "calc"
    | Ok other -> equal "ToolUse" (sprintf "%A" other)
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test snake_case union tag — fieldless multi-word case`` () =
    let codec =
        auto<ContentBlock> ()
        |> withCaseRules CaseRules.SnakeCase

    let json = codec.encode ToolResult
    let parsed = parseRaw json

    getString backend parsed "type"
    |> equal "tool_result"

    match codec.decode (parseRaw """{"type":"tool_result"}""") with
    | Ok ToolResult -> ()
    | Ok other -> equal "ToolResult" (sprintf "%A" other)
    | Error errs -> equal "Ok" (formatErrors errs)

[<Fact>]
let ``test snake_case union tag — round-trip every case`` () =
    let codec =
        auto<ContentBlock> ()
        |> withCaseRules CaseRules.SnakeCase

    let cases = [ Text { Text = "hello" }; ToolUse { Id = "id-1"; Name = "tool" }; ToolResult ]

    for original in cases do
        let json = codec.encode original

        match codec.decode (parseRaw json) with
        | Ok roundtripped -> roundtripped |> equal original
        | Error errs -> equal "Ok" (formatErrors errs)
