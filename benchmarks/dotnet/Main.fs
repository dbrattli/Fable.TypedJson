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

(**
## Nested and list-heavy payloads

`Person` is flat and three fields wide — the one shape the old codec already
pre-baked completely, so it cannot show what the plan changed. Everything
below depth 0 used to re-run `GetRecordFields` and re-derive every key on
every decode; a list of records paid that per element.

These fixtures are where that shows. `Order` nests two levels and carries a
list of records; `Catalogue` carries 100 of them, so per-element cost is 100×
magnified.

adr: measure nested separately from flat — a single flat fixture reports "no change" for a refactor that only touches depth >= 1
*)
type Address = { Street: string; City: string; Zip: string }

type Customer = {
    Name: string
    Billing: Address
    Shipping: Address
}

type LineItem = { Sku: string; Quantity: int; Price: float }

type Order = {
    Reference: string
    Buyer: Customer
    Items: LineItem list
}

[<MemoryDiagnoser>]
[<Orderer(SummaryOrderPolicy.FastestToSlowest)>]
type NestedDecodeBench() =
    let json =
        """{"reference":"A-1","buyer":{"name":"Maxime","billing":{"street":"S1","city":"Oslo","zip":"0150"},"shipping":{"street":"S2","city":"Bergen","zip":"5003"}},"items":[{"sku":"a","quantity":1,"price":9.5},{"sku":"b","quantity":2,"price":19.0},{"sku":"c","quantity":3,"price":4.25}]}"""

    let codec = auto<Order> ()

    let stjOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    [<Benchmark(Baseline = true, Description = "System.Text.Json (raw)")>]
    member _.SystemTextJson() =
        JsonSerializer.Deserialize<Order>(json, stjOptions)

    [<Benchmark(Description = "Thoth.Json.Net (auto, Newtonsoft)")>]
    member _.ThothAuto() =
        Thoth.Json.Net.Decode.Auto.fromString<Order>(json)

    [<Benchmark(Description = "Fable.TypedJson.DotNet (auto, STJ)")>]
    member _.FableTypedJson() =
        match codec.decode (parseRaw json) with
        | Ok r -> r
        | Error e -> failwithf "decode failed: %A" e

[<MemoryDiagnoser>]
[<Orderer(SummaryOrderPolicy.FastestToSlowest)>]
type NestedEncodeBench() =
    let value = {
        Reference = "A-1"
        Buyer = {
            Name = "Maxime"
            Billing = {
                Street = "S1"
                City = "Oslo"
                Zip = "0150"
            }
            Shipping = {
                Street = "S2"
                City = "Bergen"
                Zip = "5003"
            }
        }
        Items = [
            { Sku = "a"; Quantity = 1; Price = 9.5 }
            { Sku = "b"; Quantity = 2; Price = 19.0 }
            { Sku = "c"; Quantity = 3; Price = 4.25 }
        ]
    }

    let codec = auto<Order> ()

    let stjOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    [<Benchmark(Baseline = true, Description = "System.Text.Json (raw)")>]
    member _.SystemTextJson() =
        JsonSerializer.Serialize(value, stjOptions)

    [<Benchmark(Description = "Thoth.Json.Net (auto, Newtonsoft)")>]
    member _.ThothAuto() = Thoth.Json.Net.Encode.Auto.toString(0, value)

    [<Benchmark(Description = "Fable.TypedJson.DotNet (auto, STJ)")>]
    member _.FableTypedJson() = codec.encode value

(**
Codec construction on its own — the cost the other fixtures deliberately hoist
out of their measured loop, and therefore the one number nothing here reported.

It matters because the plan moved work *into* construction: a full recursive
walk of every nested record, list element type and union case, plus an eagerly
built JSON Schema tree. A consumer that calls `auto<'T> ()` per request — the
Fable.Giraffe shape — pays this every time.

adr: measure construction explicitly; "build once and reuse" is only sound advice if the cost of not doing so is known
*)
[<MemoryDiagnoser>]
[<Orderer(SummaryOrderPolicy.FastestToSlowest)>]
type ConstructionBench() =

    [<Benchmark(Baseline = true, Description = "auto<Person> () — flat")>]
    member _.Flat() = auto<Person> ()

    [<Benchmark(Description = "auto<Order> () — nested + list")>]
    member _.Nested() = auto<Order> ()

[<EntryPoint>]
let main argv =
    BenchmarkSwitcher
        .FromTypes(
            [|
                typeof<DecodeBench>
                typeof<EncodeBench>
                typeof<NestedDecodeBench>
                typeof<NestedEncodeBench>
                typeof<ConstructionBench>
            |]
        )
        .Run(argv)
    |> ignore

    0
