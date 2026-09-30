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

let scenarios = [
    "decode-record"
    "parse-decode-record"
    "encode-record"
    "strict-int"
    "strict-int64-small"
    "strict-int64-wide"
    "strict-int-reject"
]

let private require expected actual =
    if actual <> expected then
        failwithf "Incorrect benchmark result: expected %A, received %A" expected actual

    1

// decision: constructs only the selected workload so unrelated codecs do not add to command startup
// invariant: codecs and decode-only inputs are reused throughout the batch
let private createScenario name : unit -> int =
    let personJson = """{"name":"benchmark","count":42,"enabled":true}"""

    match name with
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

[<EntryPoint>]
let main (args: string array) =
    match args with
    | [| "--smoke" |] ->
        for name in scenarios do
            run name 3 0
    | [| name; iterations |] -> run name (int iterations) 10000
    | _ -> failwith "Usage: benchmark <scenario> <iterations> | --smoke"

    0
