module Fable.TypedJson.Benchmarks.Main

open Fable.TypedJson.Json

#if PYTHON
open Fable.TypedJson.Python.Json
#else
#if JS
open Fable.TypedJson.JS.Json
#else
#if DOTNET
open Fable.TypedJson.DotNet.Json
#else
open Fable.TypedJson.Beam.Json
#endif
#endif
#endif

type Person = {
    Name: string
    Count: int
    Enabled: bool
}

type IntField = { Count: int }
type Int64Field = { Count: int64 }

type WideRecord = {
    V01: int
    V02: int
    V03: int
    V04: int
    V05: int
    V06: int
    V07: int
    V08: int
    V09: int
    V10: int
    V11: int
    V12: int
    V13: int
    V14: int
    V15: int
    V16: int
}

type Tree = { Value: int; Children: Tree list }
type Group = { Items: int list }
type Groups = { Groups: Group list }

let scenarios = [
    "decode-record"
    "parse-decode-record"
    "encode-record"
    "strict-int"
    "strict-int64-small"
    "strict-int64-wide"
    "strict-int-reject"
    "construct-record"
    "decode-wide-record"
    "decode-array-128"
    "decode-array-1024"
    "decode-list-1024"
    "decode-recursive-tree"
    "reject-nested-sequence"
]

let private require expected actual =
    if actual <> expected then
        failwithf "Incorrect benchmark result: expected %A, received %A" expected actual

    1

let private decoded result =
    match result with
    | Ok value -> value
    | Error errors -> failwithf "Decode failed: %A" errors

let rec private treeJson depth =
    let children =
        if depth = 0 then
            ""
        else
            let child = treeJson (depth - 1)
            child + "," + child

    "{\"value\":1,\"children\":[" + children + "]}"

let rec private treeSum (tree: Tree) =
    tree.Value + List.sumBy treeSum tree.Children

// decision: constructs only the selected workload so unrelated codecs do not add to command startup
// invariant: parsed inputs are reused; only construct-record rebuilds its codec inside the batch
let private createScenario name : unit -> int =
    let personJson = """{"name":"benchmark","count":42,"enabled":true}"""

    match name with
    | "construct-record" ->
        let input = parseRaw personJson
        fun () -> require 42 ((auto<Person>()).decode input |> decoded).Count
    | "decode-wide-record" ->
        let codec = auto<WideRecord>()

        let fields =
            [ 1..16 ]
            |> List.map (fun i -> sprintf "\"v%02d\":%d" i i)

        let input = parseRaw ("{" + String.concat "," fields + "}")

        fun () ->
            let value = codec.decode input |> decoded

            require
                136
                (value.V01
                 + value.V02
                 + value.V03
                 + value.V04
                 + value.V05
                 + value.V06
                 + value.V07
                 + value.V08
                 + value.V09
                 + value.V10
                 + value.V11
                 + value.V12
                 + value.V13
                 + value.V14
                 + value.V15
                 + value.V16)
    | "decode-array-128"
    | "decode-array-1024"
    | "decode-list-1024" ->
        let length = if name = "decode-array-128" then 128 else 1024

        let input =
            parseRaw (
                "["
                + ([ 0 .. length - 1 ]
                   |> List.map string
                   |> String.concat ",")
                + "]"
            )

        let expected = length * (length - 1) / 2

        if name = "decode-list-1024" then
            let codec = auto<int list>()
            fun () -> require expected (codec.decode input |> decoded |> List.sum)
        else
            let codec = auto<int array>()
            fun () -> require expected (codec.decode input |> decoded |> Array.sum)
    | "decode-recursive-tree" ->
        let codec = auto<Tree>()
        let input = parseRaw (treeJson 3)
        fun () -> require 15 (codec.decode input |> decoded |> treeSum)
    | "reject-nested-sequence" ->
        let codec = autoStrict<Groups>()

        let input =
            parseRaw """{"groups":[{"items":[1,2,3]},{"items":[4,5,"invalid",7]}]}"""

        fun () ->
            match codec.decode input with
            | Error [ error ] when error.path = "groups[1].items[2]" -> 1
            | result -> failwithf "Nested sequence was not rejected correctly: %A" result
    | "decode-record"
    | "parse-decode-record" ->
        let codec = auto<Person>()
        let input = parseRaw personJson

        let decode json =
            match codec.decode json with
            | Ok value -> require 42 value.Count
            | Error errors -> failwithf "Record decode failed: %A" errors

        if name = "decode-record" then
            fun () -> decode input
        else
            fun () -> decode (parseRaw personJson)
    | "encode-record" ->
        let codec = auto<Person>()

        let value = {
            Name = "benchmark"
            Count = 42
            Enabled = true
        }

        let encoded = codec.encode value

        match codec.decode (parseRaw encoded) with
        | Ok decoded -> require value decoded |> ignore
        | Error errors -> failwithf "Encode fixture failed to round-trip: %A" errors

        fun () -> require encoded.Length (codec.encode value).Length
    | "strict-int" ->
        let codec = autoStrict<IntField>()
        let input = parseRaw """{"count":42}"""

        fun () ->
            match codec.decode input with
            | Ok value -> require 42 value.Count
            | Error errors -> failwithf "Strict int decode failed: %A" errors
    | "strict-int64-small"
    | "strict-int64-wide" ->
        let codec = autoStrict<Int64Field>()

        let expected, text =
            if name = "strict-int64-small" then
                42L, """{"count":42}"""
            else
                2147483648L, """{"count":2147483648}"""

        let input = parseRaw text

        fun () ->
            match codec.decode input with
            | Ok value -> require expected value.Count
            | Error errors -> failwithf "Strict int64 decode failed: %A" errors
    | "strict-int-reject" ->
        let codec = autoStrict<IntField>()
        let input = parseRaw """{"count":2147483648}"""

        fun () ->
            match codec.decode input with
            | Error [ error ] when error.path = "count" -> 1
            | result -> failwithf "Out-of-range int was not rejected correctly: %A" result
    | _ -> failwithf "Unknown scenario '%s'. Available: %A" name scenarios

let private run name iterations warmup =
    if iterations <= 0 then
        failwith "Iteration count must be positive"

    if warmup < 0 then
        failwith "Warmup count must be non-negative"

    let operation = createScenario name

    // decision: warms the workload within each process because CodSpeed CLI warmups launch fresh processes
    for _ in 1..warmup do
        operation () |> ignore

    // invariant: consumes and checks every result so failed work cannot produce a faster benchmark
    let mutable checksum = 0

    for _ in 1..iterations do
        checksum <- checksum + operation ()

    require iterations checksum |> ignore
    printfn "%s: %d operations, checksum %d" name iterations checksum

#if CODSPEED_THROUGHPUT
// decision: shares checked fixtures with the JS throughput harness so only the measurement boundary changes
let prepareScenario name = createScenario name
#else
[<EntryPoint>]
let main (args: string array) =
    match args with
    | [| "--smoke" |] ->
        for name in scenarios do
            run name 3 0
    | [| name; iterations |] ->
        // decision: bounds warmup for new structural workloads so quadratic traversal and recursive planning remain affordable
        let warmup =
            match name with
            | "construct-record"
            | "decode-wide-record"
            | "decode-array-128"
            | "decode-array-1024"
            | "decode-list-1024"
            | "decode-recursive-tree"
            | "reject-nested-sequence" -> 10
            | _ -> 10000

        run name (int iterations) warmup
    | [| name; iterations; warmup |] -> run name (int iterations) (int warmup)
    | _ -> failwith "Usage: benchmark <scenario> <iterations> [warmup] | --smoke"

    0
#endif
