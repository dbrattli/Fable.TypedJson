# Architecture

[Overview](overview.md) · [JSON Schema](json-schema.md) · [Performance →](performance.md)

The library separates model planning from runtime execution, and separates the
shared F# workflow from each target's JSON representation.

## Planning and execution

Constructing a codec resolves types, field readers, constructors, JSON keys,
union tags, and registered codecs into plan nodes. Each node contains decode
and encode closures plus schema data. Repeated operations run those closures
rather than repeating structural dispatch for non-recursive paths.

Recursive types use deferred plan resolution so construction terminates. On
.NET, JavaScript, and Python, resolved levels are retained lazily in the codec.
BEAM rebuilds deferred levels because the process-local references used by
lazy values cannot safely travel with a codec. Consequently, recursive
traversal can still perform type planning during decode or encode.

Schema entry points invoke the same planner with the supplied registry and
naming configuration. They build a new plan rather than reading the retained
runtime codec's plan. Definitions mode adds references and collected schema
definitions without introducing a separate type-walking algorithm.

## Module responsibilities

| Module | Responsibility |
| --- | --- |
| `Schema` | Shared JSON value, error, schema, codec, and registry types |
| `Codec` | Primitive custom codecs and constraint combinators |
| `Refined` | Bundled wrapper types and their codecs |
| `Plan` | Shared structural planning and runtime validation |
| `Json` (`TypedJson.fs`) | Public codecs, naming, aliases, shortcuts, and model validators |
| `JsonSchemaGen` | Schema entry points, definition naming, and rendering |
| Target `Backend` | Native JSON operations through `IJsonBackend` |
| Target `Json` | Convenience API with the backend pre-applied |

Built-in plan nodes operate on native backend values. The portable `JsonValue`
union is introduced at the custom-codec boundary for `IJsonCodec.Decode` and
`Encode`; built-in primitive decoding does not allocate it for every value.

## Backend contract

`IJsonBackend` owns parsing, serialization, native maps and arrays, null,
type tests, and typed accessors. A new target implements that interface and
provides a convenience `Json` module. The core has no parser dependency on a
specific target.

Three details matter when implementing a backend:

- `TryGet(map, key)` returns `None` only for a missing key. A present JSON null
  must return `Some` of the backend's null value. `ContainsKey` followed by
  `Get` is a valid implementation; fused native lookup avoids two searches.
- `Put` returns the map to use for the next operation. Callers must use the
  returned value and stop using the previous accumulator, allowing immutable
  maps or in-place mutation behind the same interface.
- `Null` must be the runtime's JSON null representation. F# `null` is not a
  portable replacement, particularly on BEAM.

`ArrayMapper` is optional. Return `None` for indexed traversal through
`ArrayLength` and `ArrayAt`, or supply `IJsonArrayMapper` for native traversal.
Its callback visits elements in order, stops at the first error, and returns
the failing zero-based index. Empty input produces an empty list, successful
null results remain values, and callback exceptions propagate. The shared
plan still owns type validation and error paths.

## Optimization boundaries

The core's `Optimizations/` folder contains specialized traversal, deferred
plan reuse, runtime reflection and representation helpers, record buffers,
and numeric compatibility helpers. Compiler conditionals remain inside those
helpers; runtime capabilities are selected while constructing codecs.

Substantial target-specific algorithms live in the adapter's native language:
Erlang, JavaScript, or Python. Adapter optimization F# files contain bindings
and capability wiring. Small native expressions can use `Emit` in a helper;
JavaScript record-buffer allocation needs no separate distribution asset.

BEAM's native sequence traversal lives in
[`typedjson_beam_sequences.erl`](../src/Fable.TypedJson.Beam/Optimizations/typedjson_beam_sequences.erl),
with [`Bindings.fs`](../src/Fable.TypedJson.Beam/Optimizations/Bindings.fs)
exposing `IJsonArrayMapper`. Local recipes copy native modules into generated
application `src/` directories. The package includes them under `fable/src/`,
so consumers build them through ordinary `rebar3 compile` without a repository
copy recipe.

Validation semantics and schema generation remain shared. BEAM, JavaScript,
and Python are the primary performance targets; .NET is also supported for
execution and validation. See [Contributing](../CONTRIBUTING.md) for checks
and rules when changing these boundaries.
