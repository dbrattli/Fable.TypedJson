# Performance

[Overview](overview.md) · [Architecture](architecture.md) · [Benchmark harness](../benchmarks/cli/README.md)

The most useful application optimization is retaining the finished codec.
Construction performs type planning; repeated calls can reuse the result.

## Codec ownership

Build the registry, codec, aliases, case rules, and model validators outside
the repeated operation. JavaScript, Python, and .NET can retain a module-level
codec. On BEAM, create it during application or actor startup and retain it
in that owner, passing the codec or a handler closure where needed. Compiled
module-level accessors can rebuild their values when called.

`withCaseRules` and `alias` rebuild codecs. A one-off `decodeWith` or
`encodeWith` override also builds a plan when the requested rule differs from
the default. Retain separate configured codecs when formats are reused.
`validateMap*`, `validateJson*`, and `dump*` build a plan per call; schema entry
points likewise build a new plan. They are convenient for occasional work.

Recursive models have deferred planning costs. JavaScript, Python, and .NET
retain resolved levels in the codec; BEAM rebuilds deferred levels during
traversal. See [Architecture](architecture.md) for the lifecycle distinction.

There is no implicit global BEAM codec cache. A safe identity would need to
cover the type, registry, case rules, aliases, model validators, and code-upgrade
lifecycle. Keep ownership explicit in your application.

## What the benchmark suites measure

The [CodSpeed harness guide](../benchmarks/cli/README.md) owns workload names,
counts, runtime settings, profiles, and CI instructions. The shared CLI suite
measures a whole fixed batch, including process startup, setup, warmup, and
output. Compilation and dependency restoration happen beforehand. Compare
each workload with its own history and matching settings, rather than comparing
raw batch durations across targets.

The JS-only throughput profile isolates checked decode batches within one
process. Its results have a separate history from whole-process measurements.
BenchmarkDotNet supplies .NET per-operation timing and allocation diagnostics:

```sh
just bench --job short
```

The benchmark fixtures live in
[`benchmarks/dotnet/Main.fs`](../benchmarks/dotnet/Main.fs). Keep construction,
parsing, decoding, and serialization costs distinct when interpreting results.
Local timings are smoke evidence; use completed comparisons with the same
runner and settings to assess a performance change.

## Historical .NET snapshot

These figures are preserved from the previous README's BenchmarkDotNet
`DefaultJob` run on .NET 10. They were not rerun for this documentation change,
and the original README did not record a benchmark commit or machine profile.
Treat them as an illustration of the measured fixtures, not current guarantees.
Ratios also depend on the machine, runtime, workload, and library versions.

| Flat three-field record, parse and decode | Mean | Allocated | Ratio to its parser baseline |
| --- | ---: | ---: | ---: |
| System.Text.Json, raw parse | 153 ns | 224 B | — |
| Thoth.Json.STJ, hand-written decoder | 442 ns | 976 B | 2.9× |
| Newtonsoft, raw parse | 467 ns | 3,056 B | — |
| Fable.TypedJson, `auto` | 518 ns | 1,072 B | 3.4× |
| Thoth.Json.Net, `Decode.Auto` | 7,224 ns | 8,785 B | 15.5× |

The fixtures used different parsers for the two automatic paths:
System.Text.Json for TypedJson and Newtonsoft for Thoth.Json.Net. Their
end-to-end difference therefore includes parsing as well as codec execution.
Normalizing by each parser baseline helps contextualize the numbers, but does
not isolate decoder cost or establish a portable speedup.

| Other fixtures | Fable.TypedJson | Thoth.Json.Net, `Auto` | TypedJson ratio to System.Text.Json baseline |
| --- | ---: | ---: | ---: |
| Nested decode: two levels and a record list | 2.92 µs | 34.56 µs | 2.2× |
| Flat encode | 252 ns | 8,679 ns | 2.7× |
| Nested encode | 6.67 µs | 46.58 µs | 10.1× |

The old snapshot also reported a 592 ns manual Thoth flat encoder and codec
construction costs of roughly 193 µs for the flat model and 1.03 ms for the
nested one. Construction was outside the repeated decode/encode loops. Those
costs reinforce the ownership guidance, but do not establish a universal
number of operations at which codec construction pays off.
