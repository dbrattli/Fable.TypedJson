(**
# TestErasedDU — Verify erased DU pattern matching on BEAM

Tests that erased DUs can be used as function parameters
with pattern matching, compiling to Erlang type guard clauses.
*)

module Fable.TypedJson.Tests.ErasedDU

open Fable.Core
open Fable.TypedJson.Testing

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

[<Fact>]
let ``test erased DU match on string`` () =
    classify (JString "hello") |> equal "string:hello"

[<Fact>]
let ``test erased DU match on int`` () = classify (JInt 42) |> equal "int:42"

[<Fact>]
let ``test erased DU match on float`` () =
    classify (JFloat 3.14) |> equal "float:3.1"

[<Fact>]
let ``test erased DU match on bool`` () =
    classify (JBool true) |> equal "bool:true"

// ============================================================================
// Erased DU stored in F# Map and retrieved
// ============================================================================

[<Fact>]
let ``test erased DU in Map`` () =
    let m: Map<string, JsonValue> =
        Map.ofList [
            "name", JString "Alice"
            "age", JInt 30
            "score", JFloat 9.5
            "active", JBool true
        ]

    match Map.tryFind "name" m with
    | Some(JString s) -> s |> equal "Alice"
    | _ -> equal "JString" "other"

[<Fact>]
let ``test erased DU in Map int`` () =
    let m: Map<string, JsonValue> = Map.ofList [ "age", JInt 30 ]

    match Map.tryFind "age" m with
    | Some(JInt n) -> n |> equal 30
    | _ -> equal "JInt" "other"

[<Fact>]
let ``test erased DU in Map float`` () =
    let m: Map<string, JsonValue> = Map.ofList [ "score", JFloat 9.5 ]

    match Map.tryFind "score" m with
    | Some(JFloat f) -> f |> equal 9.5
    | _ -> equal "JFloat" "other"

[<Fact>]
let ``test erased DU in Map missing key`` () =
    let m: Map<string, JsonValue> = Map.ofList [ "name", JString "Alice" ]

    match Map.tryFind "missing" m with
    | None -> () // expected
    | Some _ -> equal "None" "Some"

// ============================================================================
// Simulating jsx.decode output: unbox raw BEAM values to erased DU
// ============================================================================

[<Fact>]
let ``test unbox string to erased DU`` () =
    let raw: obj = box "hello"
    let v: JsonValue = unbox raw

    match v with
    | JString s -> s |> equal "hello"
    | _ -> equal "JString" "other"

[<Fact>]
let ``test unbox int to erased DU`` () =
    let raw: obj = box 42
    let v: JsonValue = unbox raw

    match v with
    | JInt n -> n |> equal 42
    | _ -> equal "JInt" "other"

[<Fact>]
let ``test unbox float to erased DU`` () =
    let raw: obj = box 3.14
    let v: JsonValue = unbox raw

    match v with
    | JFloat f -> f |> equal 3.14
    | _ -> equal "JFloat" "other"

[<Fact>]
let ``test unbox bool to erased DU`` () =
    let raw: obj = box true
    let v: JsonValue = unbox raw

    match v with
    | JBool b -> b |> equal true
    | _ -> equal "JBool" "other"
