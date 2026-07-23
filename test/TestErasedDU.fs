(**
# TestErasedDU — Verify erased DU pattern matching on BEAM

Tests that erased DUs can be used as function parameters
with pattern matching, compiling to Erlang type guard clauses.
*)

module Fable.TypedJson.Tests.ErasedDU

open Fable.Core
open Fable.TypedJson.Testing
open Scriptorium.Quill
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

[<Erase>]
type JsonValue =
    | JString of string
    | JInt of int
    | JFloat of float
    | JBool of bool

// ============================================================================
// Basic pattern matching on erased DU as function parameter
// ============================================================================

let classify (v: JsonValue) : string =
    match v with
    | JString s -> sprintf "string:%s" s
    | JInt n -> sprintf "int:%d" n
    | JFloat f -> sprintf "float:%.1f" f
    | JBool b -> sprintf "bool:%b" b


let private basicPatternMatchingTests =
    testList (
        "Basic pattern matching on erased DU as function parameter",
        [
            test ("erased DU match on string", fun _ -> assertThat (classify (JString "hello")) (isEqualTo "string:hello"))
            test ("erased DU match on int", fun _ -> assertThat (classify (JInt 42)) (isEqualTo "int:42"))

            // JS has no runtime int vs. float distinction (all `number`), so the JInt
            // guard matches a JFloat value first and the test would compare float to int.
            test ("erased DU match on float", skipIfJavaScript, fun _ -> assertThat (classify (JFloat 3.14)) (isEqualTo "float:3.1"))

            test ("erased DU match on bool", fun _ -> assertThat (classify (JBool true)) (isEqualTo "bool:true"))
        ]
    )

// ============================================================================
// Erased DU stored in F# Map and retrieved
// ============================================================================

let private erasedDUStoredTests =
    testList (
        "Erased DU stored in F# Map and retrieved",
        [
            test (
                "erased DU in Map",
                fun _ ->
                    let m: Map<string, JsonValue> =
                        Map.ofList [
                            "name", JString "Alice"
                            "age", JInt 30
                            "score", JFloat 9.5
                            "active", JBool true
                        ]

                    match Map.tryFind "name" m with
                    | Some(JString s) -> assertThat s (isEqualTo "Alice")
                    | _ -> assertThat "other" (isEqualTo "JString")
            )
            test (
                "erased DU in Map int",
                fun _ ->
                    let m: Map<string, JsonValue> = Map.ofList [ "age", JInt 30 ]

                    match Map.tryFind "age" m with
                    | Some(JInt n) -> assertThat n (isEqualTo 30)
                    | _ -> assertThat "other" (isEqualTo "JInt")
            )
            test (
                "erased DU in Map float",
                fun _ ->
                    let m: Map<string, JsonValue> = Map.ofList [ "score", JFloat 9.5 ]

                    match Map.tryFind "score" m with
                    | Some(JFloat f) -> assertThat f (isEqualTo 9.5)
                    | _ -> assertThat "other" (isEqualTo "JFloat")
            )
            test (
                "erased DU in Map missing key",
                fun _ ->
                    let m: Map<string, JsonValue> = Map.ofList [ "name", JString "Alice" ]

                    match Map.tryFind "missing" m with
                    | None -> () // expected
                    | Some _ -> assertThat "Some" (isEqualTo "None")
            )
        ]
    )

// ============================================================================
// Simulating jsx.decode output: unbox raw BEAM values to erased DU
//
// These tests assert Fable-specific erasure semantics: `unbox<JsonValue>
// (box "hello")` works because Fable erases JsonValue and the boxed string
// IS the JsonValue at runtime. The CLR doesn't erase, so the cast fails
// with InvalidCastException — skip on the .NET target.
// ============================================================================

let private simulatingJsxDecodeTests =
    testList (
        "Simulating jsx.decode output: unbox raw BEAM values to erased DU",
        skipIfDotNet,
        [
            test (
                "unbox string to erased DU",
                fun _ ->
                    let raw: obj = box "hello"
                    let v: JsonValue = unbox raw

                    match v with
                    | JString s -> assertThat s (isEqualTo "hello")
                    | _ -> assertThat "other" (isEqualTo "JString")
            )
            test (
                "unbox int to erased DU",
                fun _ ->
                    let raw: obj = box 42
                    let v: JsonValue = unbox raw

                    match v with
                    | JInt n -> assertThat n (isEqualTo 42)
                    | _ -> assertThat "other" (isEqualTo "JInt")
            )
            test (
                "unbox float to erased DU",
                fun _ ->
                    let raw: obj = box 3.14
                    let v: JsonValue = unbox raw

                    match v with
                    | JFloat f -> assertThat f (isEqualTo 3.14)
                    | _ -> assertThat "other" (isEqualTo "JFloat")
            )
            test (
                "unbox bool to erased DU",
                fun _ ->
                    let raw: obj = box true
                    let v: JsonValue = unbox raw

                    match v with
                    | JBool b -> assertThat b isTrue
                    | _ -> assertThat "other" (isEqualTo "JBool")
            )
        ]
    )

let tests =
    testList ("ErasedDU", [ basicPatternMatchingTests; erasedDUStoredTests; simulatingJsxDecodeTests ])
