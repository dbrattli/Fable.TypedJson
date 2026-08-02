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
// Tagged DU as the top-level type
// ============================================================================

type SearchInput = { Query: string; MaxResults: int }
type CalcInput = { Expression: string }

type Tool =
    | Search of SearchInput
    | Calculate of CalcInput
    | Ping

let private topLevelUnionTests =
    testList (
        "Tagged DU as the top-level type",
        [
            test (
                "decode tagged union top-level — search case",
                fun _ ->
                    let codec = auto<Tool> ()
                    let map = parseRaw """{"type":"search","query":"hello","maxResults":5}"""

                    match codec.decode map with
                    | Ok(Search input) ->
                        assertThat input.Query (isEqualTo "hello")
                        assertThat input.MaxResults (isEqualTo 5)
                    | Ok other -> assertThat (sprintf "%A" other) (isEqualTo "Search")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "decode tagged union top-level — calculate case",
                fun _ ->
                    let codec = auto<Tool> ()
                    let map = parseRaw """{"type":"calculate","expression":"1+1"}"""

                    match codec.decode map with
                    | Ok(Calculate input) -> assertThat input.Expression (isEqualTo "1+1")
                    | Ok other -> assertThat (sprintf "%A" other) (isEqualTo "Calculate")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "decode tagged union — fieldless case",
                fun _ ->
                    let codec = auto<Tool> ()
                    let map = parseRaw """{"type":"ping"}"""

                    match codec.decode map with
                    | Ok Ping -> ()
                    | Ok other -> assertThat (sprintf "%A" other) (isEqualTo "Ping")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "decode tagged union — unknown discriminator value",
                fun _ ->
                    let codec = auto<Tool> ()
                    let map = parseRaw """{"type":"bogus","query":"x"}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs
                        assertThat (formatted.Contains("bogus")) isTrue
            )
            test (
                "decode tagged union — missing discriminator",
                fun _ ->
                    let codec = auto<Tool> ()
                    let map = parseRaw """{"query":"x"}"""

                    match codec.decode map with
                    | Ok _ -> assertThat "Ok" (isEqualTo "Error")
                    | Error errs ->
                        let formatted = formatErrors errs
                        assertThat (formatted.Contains("type")) isTrue
            )
            test (
                "encode tagged union — search case",
                fun _ ->
                    let codec = auto<Tool> ()
                    let value = Search { Query = "hello"; MaxResults = 5 }
                    let json = codec.encode value
                    let parsed = parseRaw json

                    assertThat (getString backend parsed "type") (isEqualTo "search")
                    assertThat (getString backend parsed "query") (isEqualTo "hello")
                    assertThat (getInt backend parsed "maxResults") (isEqualTo 5)
            )
            test (
                "encode tagged union — fieldless case",
                fun _ ->
                    let codec = auto<Tool> ()
                    let json = codec.encode Ping
                    let parsed = parseRaw json

                    assertThat (getString backend parsed "type") (isEqualTo "ping")
            )
            test (
                "round-trip tagged union — every case",
                fun _ ->
                    let codec = auto<Tool> ()

                    let cases = [
                        Search { Query = "q"; MaxResults = 3 }
                        Calculate { Expression = "2*3" }
                        Ping
                    ]

                    for original in cases do
                        let json = codec.encode original

                        match codec.decode (parseRaw json) with
                        | Ok roundtripped -> assertThat roundtripped (isEqualTo original)
                        | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// Tagged DU as a record field
// ============================================================================

type Envelope = { Id: int; Tool: Tool }

let private unionAsRecordFieldTests =
    testList (
        "Tagged DU as a record field",
        [
            test (
                "tagged union as record field — round-trip",
                fun _ ->
                    let codec = auto<Envelope> ()

                    let original = {
                        Id = 42
                        Tool = Search { Query = "fsharp"; MaxResults = 10 }
                    }

                    let json = codec.encode original

                    match codec.decode (parseRaw json) with
                    | Ok roundtripped -> assertThat roundtripped (isEqualTo original)
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
        ]
    )

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

let private snakeCaseDiscriminatorTests =
    testList (
        "Snake-case discriminator tags (Anthropic content-block style)",
        [
            test (
                "snake_case union tag — encode multi-word case",
                fun _ ->
                    let codec =
                        auto<ContentBlock> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let value = ToolUse { Id = "abc"; Name = "search" }
                    let json = codec.encode value
                    let parsed = parseRaw json

                    assertThat (getString backend parsed "type") (isEqualTo "tool_use")

                    assertThat (getString backend parsed "id") (isEqualTo "abc")
                    assertThat (getString backend parsed "name") (isEqualTo "search")
            )
            test (
                "snake_case union tag — decode multi-word case",
                fun _ ->
                    let codec =
                        auto<ContentBlock> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let map = parseRaw """{"type":"tool_use","id":"xyz","name":"calc"}"""

                    match codec.decode map with
                    | Ok(ToolUse p) ->
                        assertThat p.Id (isEqualTo "xyz")
                        assertThat p.Name (isEqualTo "calc")
                    | Ok other -> assertThat (sprintf "%A" other) (isEqualTo "ToolUse")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "snake_case union tag — fieldless multi-word case",
                fun _ ->
                    let codec =
                        auto<ContentBlock> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let json = codec.encode ToolResult
                    let parsed = parseRaw json

                    assertThat (getString backend parsed "type") (isEqualTo "tool_result")

                    match codec.decode (parseRaw """{"type":"tool_result"}""") with
                    | Ok ToolResult -> ()
                    | Ok other -> assertThat (sprintf "%A" other) (isEqualTo "ToolResult")
                    | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
            test (
                "snake_case union tag — round-trip every case",
                fun _ ->
                    let codec =
                        auto<ContentBlock> ()
                        |> withCaseRules CaseRules.SnakeCase

                    let cases = [ Text { Text = "hello" }; ToolUse { Id = "id-1"; Name = "tool" }; ToolResult ]

                    for original in cases do
                        let json = codec.encode original

                        match codec.decode (parseRaw json) with
                        | Ok roundtripped -> assertThat roundtripped (isEqualTo original)
                        | Error errs -> assertThat (formatErrors errs) (isEqualTo "Ok")
            )
        ]
    )

// ============================================================================
// Unsupported union shapes are rejected at construction
// ============================================================================

(**
v1 supports fieldless cases and single-record-payload cases. Every other shape
used to fail in two different ways at two different times: decode returned a
`FieldError` when a document happened to select that case, while encode emitted
a bare `{"type": "..."}` and silently discarded the payload, producing JSON
that could not be read back.

The walker now resolves every case when the codec is built, so an unsupported
one is rejected there. A DU with a case that cannot round-trip is a broken
codec whether or not a given document reaches that case, and finding out at
construction beats finding out on the first request that uses it.

A wrapper type that wants real encoding should register an `IJsonCodec`, which
takes precedence over union dispatch on both paths.

invariant: an unsupported union shape fails at codec construction, never silently at run time
adr: reject at construction rather than per value — the failure is a property of the type, not the document
*)
type Shape =
    | Circle of float
    | Square of float

type Segment = At of int * int

let private unsupportedUnionShapeTests =
    testList (
        "Unsupported union shapes are rejected at construction",
        [
            test (
                "non-record single-field case is rejected when the codec is built",
                fun _ -> assertThat (fun () -> auto<Shape> () |> ignore) throws
            )
            test (
                "multi-positional-field case is rejected when the codec is built",
                fun _ -> assertThat (fun () -> auto<Segment> () |> ignore) throws
            )
            test (
                "a union of supported shapes still builds",
                fun _ ->
                    // Guards the rejection against over-reach: fieldless and
                    // single-record-payload cases must stay buildable.
                    // Asserted on the parsed value, not the JSON text — the
                    // backends differ in separator spacing.
                    let codec = auto<Tool> ()
                    let parsed = parseRaw (codec.encode Ping)
                    assertThat (getString backend parsed "type") (isEqualTo "ping")
            )
        ]
    )

let tests =
    testList (
        "Union",
        [
            topLevelUnionTests
            unionAsRecordFieldTests
            snakeCaseDiscriminatorTests
            unsupportedUnionShapeTests
        ]
    )
