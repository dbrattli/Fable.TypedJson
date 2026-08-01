(**
# Fable.TypedJson.DotNet — sanity-check benchmark

Not a competitive comparison: System.Text.Json's serializer goes straight
from the JSON bytes to a typed record via source-generated property
setters, while this library walks reflection over a parsed `JsonValue`
tree. The expected ratio is large.

The point is to spot accidental pathologies — quadratic walks, repeated
allocations, type-test misses — by comparing against a known-good
baseline. A 5-10× ratio is fine for what the library does; 100× would
mean something is wrong.
*)

module Bench

open System.Text.Json
open System.Text.Json.Serialization
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Order
open BenchmarkDotNet.Running
open Thoth.Json.Core
open Fable.TypedJson.Json
open Fable.TypedJson.DotNet.Json

// 3-field Person record. We compare four code paths against the same
// JSON literal:
//   1. raw System.Text.Json (source-gen-style baseline — no DOM tree,
//      direct property setters)
//   2. Thoth.Json.System.Text.Json with hand-written `Decode.object` —
//      manual decoder, no reflection
//   3. Thoth.Json.Net with `Decode.Auto.fromString<T>` — reflection-
//      driven, Newtonsoft.Json-backed (the only Thoth path that surfaces
//      Auto on plain .NET; `Thoth.Json.Decode.Auto` from the high-level
//      package is Fable-only and throws "dummy code" on the CLR)
//   4. Fable.TypedJson.DotNet — reflection-driven `auto<'T>`
//
// (3) is the closest analog to (4) — both are reflection-driven over
// F# records. They use different parsing backends (Newtonsoft vs STJ),
// so comparing absolute numbers should account for that.
type Person = {
    Firstname: string
    Surname: string
    Age: int
}

let personDecoder: Decoder<Person> =
    Decode.object (fun get -> {
        Firstname = get.Required.Field "firstname" Decode.string
        Surname = get.Required.Field "surname" Decode.string
        Age = get.Required.Field "age" Decode.int
    })

[<MemoryDiagnoser>]
[<Orderer(SummaryOrderPolicy.FastestToSlowest)>]
type DecodeBench() =
    // Property names are camelCase in the JSON to match both libraries'
    // configured naming policies — keeps the comparison apples-to-apples.
    let json = """{"firstname":"Maxime","surname":"Mangel","age":29}"""

    // Build the codec once per benchmark instance, NOT per iteration —
    // codec construction allocates a closure tree that we don't want to
    // amortize into the per-decode measurement.
    let codec = auto<Person> ()

    let stjOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    [<Benchmark(Baseline = true, Description = "System.Text.Json (raw)")>]
    member _.SystemTextJson() =
        JsonSerializer.Deserialize<Person>(json, stjOptions)

    [<Benchmark(Description = "Thoth.Json.STJ (manual)")>]
    member _.ThothManual() =
        Thoth.Json.System.Text.Json.Decode.fromString personDecoder json

    [<Benchmark(Description = "Thoth.Json.Net (auto, Newtonsoft)")>]
    member _.ThothAuto() =
        Thoth.Json.Net.Decode.Auto.fromString<Person>(json)

    [<Benchmark(Description = "Fable.TypedJson.DotNet (auto, STJ)")>]
    member _.FableTypedJson() =
        match codec.decode (parseRaw json) with
        | Ok r -> r
        | Error e -> failwithf "decode failed: %A" e

[<MemoryDiagnoser>]
[<Orderer(SummaryOrderPolicy.FastestToSlowest)>]
type EncodeBench() =
    let value = {
        Firstname = "Maxime"
        Surname = "Mangel"
        Age = 29
    }

    let codec = auto<Person> ()

    let stjOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    // Build a Thoth `IEncodable` tree via the backend-agnostic
    // `Thoth.Json.Core.Encode.*` primitives, then render through the
    // STJ-specific `Thoth.Json.System.Text.Json.Encode.toString`. Note:
    // `Thoth.Json.Encode.object` from the high-level Thoth.Json package
    // is *Fable-only* — it calls into JsInterop and throws on .NET. The
    // .NET-portable encoders live under `Thoth.Json.Core.Encode`.
    let thothEncode (p: Person) : string =
        let tree =
            Thoth.Json.Core.Encode.object [
                "firstname", Thoth.Json.Core.Encode.string p.Firstname
                "surname", Thoth.Json.Core.Encode.string p.Surname
                "age", Thoth.Json.Core.Encode.int p.Age
            ]

        Thoth.Json.System.Text.Json.Encode.toString 0 tree

    [<Benchmark(Baseline = true, Description = "System.Text.Json (raw)")>]
    member _.SystemTextJson() =
        JsonSerializer.Serialize(value, stjOptions)

    [<Benchmark(Description = "Thoth.Json.STJ (manual)")>]
    member _.ThothManual() = thothEncode value

    [<Benchmark(Description = "Thoth.Json.Net (auto, Newtonsoft)")>]
    member _.ThothAuto() = Thoth.Json.Net.Encode.Auto.toString(0, value)

    [<Benchmark(Description = "Fable.TypedJson.DotNet (auto, STJ)")>]
    member _.FableTypedJson() = codec.encode value

[<EntryPoint>]
let main argv =
    BenchmarkSwitcher
        .FromTypes([| typeof<DecodeBench>; typeof<EncodeBench> |])
        .Run(argv)
    |> ignore

    0
