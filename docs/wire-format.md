# Wire format

[Overview](overview.md) · [Validation](validation.md) · [JSON Schema →](json-schema.md)

The wire format describes JSON keys and value shapes. Configure it on the codec
so decoding, encoding, and schema generation use the same naming rules.

## Case rules

```fsharp
open System
open Fable.TypedJson.Schema
open Fable.TypedJson.Json
open Fable.TypedJson.Beam.Json

type Reading = { Location: string; AirTemperature: float }

let reading = { Location = "Oslo"; AirTemperature = 22.5 }
let codec = auto<Reading> () |> withCaseRules CaseRules.SnakeCase
let json = codec.encode reading
// JSON text: {"location":"Oslo","air_temperature":22.5}
```

| Rule | Example input | JSON key |
| --- | --- | --- |
| `None` | `AirTemperature` | `AirTemperature` |
| `LowerFirst` (default) | `AirTemperature` | `airTemperature` |
| `SnakeCase` | `AirTemperature` | `air_temperature` |
| `SnakeCaseAllCaps` | `AirTemperature` | `AIR_TEMPERATURE` |
| `KebabCase` | `AirTemperature` | `air-temperature` |
| `PascalCase` | `air_temperature` | `AirTemperature` |

Except for `None`, names are normalized through PascalCase before applying the
rule. The separator rules split before each uppercase letter; for example,
`URLValue` becomes `u_r_l_value` under `SnakeCase`.

`withCaseRules` rebuilds the codec. For repeated use of two formats, retain two
configured codecs. `decodeWith` and `encodeWith` can override the case rule for
one call; a different rule builds a plan for that call.

## Aliases

```fsharp
let aliasedCodec = codec |> alias "AirTemperature" "temperature_c"
let aliasedJson = aliasedCodec.encode reading
let aliasedSchema = jsonSchemaOfCodec emptyRegistry aliasedCodec
```

An alias overrides the case rule for that field. It applies to JSON decoding,
string-map lookup, encoding, and schema `properties` / `required` keys. Matching
accepts the configured key exactly, without accepting the original spelling as
another option.

Alias names are canonicalized to PascalCase. The alias map belongs to the whole
codec, so it applies to matching F# field names in nested records too; it is not
a path-specific override. Avoid aliases that make two fields share a JSON key
or collide with the tagged-union discriminator `type`.

## Optional fields and null

```fsharp
type Contact = { Name: string; Email: string option }

let contactCodec = auto<Contact> ()
let missing = decodeText contactCodec """{"name":"Ada"}"""
let explicitNull = decodeText contactCodec """{"name":"Ada","email":null}"""
// Both return Ok { Name = "Ada"; Email = None }

let omitted = contactCodec.encode { Name = "Ada"; Email = None }
// JSON text: {"name":"Ada"}
```

Required fields reject missing values and null. Optional record fields accept
both as `None`, and encode `Some value` using the inner codec. Schema generation
omits optional fields from `required` and describes their inner value type; it
does not add a nullable union. Thus the decoder accepts null for an optional
field even when the emitted schema describes only its non-null form.

The manual `Encode.optional` helper has different output semantics: it emits
JSON null for `None`. Use the target adapter's `Encode` helpers to build manual
JSON objects, arrays, optional values, raw fragments, and text.

## Records and collections

Nested records become nested JSON objects. F# lists and arrays become JSON
arrays and preserve element order. Their element types can themselves be
records, supported unions, or registered custom types. Recursive records and
supported unions decode and encode finite JSON structures; schema handling is
explained in [Recursive types](json-schema.md#recursive-types-and-definitions).

Extra object properties are ignored during decoding. This also applies to
`autoStrict`; strictness concerns primitive types.

## Scalar values

| F# type | Encoded JSON | Schema |
| --- | --- | --- |
| `string`, `int`, `int64`, `float`, `bool` | Corresponding JSON primitive | `string`, `integer`, `number`, or `boolean` |
| `DateTime` | UTC timestamp string ending in `Z` | `string`, format `date-time` |
| `DateTimeOffset` | UTC timestamp string ending in `Z` | `string`, format `date-time` |
| `Guid` | Canonical UUID string | `string`, format `uuid` |
| `decimal` | Decimal string | `string`, format `decimal` |

Timestamp decoding converts offsets to a UTC instant and treats timestamps
without a zone as UTC. Encoding a `DateTime` uses `ToUniversalTime`; use a UTC
`DateTime` or `DateTimeOffset` for an explicit instant. Original offsets and
`DateTime.Kind` are not preserved by a round trip.

Decimal strings avoid passing through a JSON binary floating-point number.
Decoding also accepts JSON numbers, including in strict mode, but those may
already have lost precision in the backend parser. `decimal` is a descriptive
format annotation, not a standard validation rule. Integer precision likewise
depends on the target's native number representation; do not assume every
runtime can preserve arbitrary `int64` JSON numbers exactly.

## Tagged discriminated unions

```fsharp
type SearchInput = { Query: string; MaxResults: int }

type Tool =
    | Search of SearchInput
    | Ping

let toolCodec = auto<Tool> () |> withCaseRules CaseRules.SnakeCase
let search =
    decodeText toolCodec """{"type":"search","query":"hello","max_results":5}"""
// Ok (Search { Query = "hello"; MaxResults = 5 })

let ping = toolCodec.encode Ping
// JSON text: {"type":"ping"}
```

The discriminator key is always `type`. Case rules transform the case name,
so `ToolUse` becomes `tool_use` under `SnakeCase`. A single record payload
flattens alongside the discriminator; a fieldless case contains only the tag.
The schema uses `oneOf`, with a `const` discriminator per case.

Cases with non-record payloads (`Circle of float`) or multiple positional
fields (`At of int * int`) fail when the codec is constructed. Wrap the payload
in a record or register a custom codec for the union. Other automatic shapes,
such as tuples, maps, or arbitrary classes, need custom codecs for decoding.
