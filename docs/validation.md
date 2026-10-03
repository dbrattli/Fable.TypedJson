# Validation

[Overview](overview.md) · [Decoding](decoding.md) · [Wire format →](wire-format.md)

Give a field a dedicated type when its rules should apply wherever that type is
used. Define its `IJsonCodec`, register it explicitly, and derive the containing
record codec with that registry.

## A custom field type

```fsharp
open Fable.TypedJson
open Fable.TypedJson.Schema
open Fable.TypedJson.Refined
open Fable.TypedJson.Json
open Fable.TypedJson.Beam.Json

type Days =
    | Days of int

    static member JsonCodec: IJsonCodec<Days> =
        Codec.int
        |> Codec.gt 0
        |> Codec.le 14
        |> Codec.map Days (fun (Days n) -> n)

type WeatherRequest = {
    Location: NonEmptyString
    Days: Days
    Detailed: bool option
}

let registry = emptyRegistry |> register Days.JsonCodec |> registerAll
let codec = autoWith<WeatherRequest> registry

let valid = decodeText codec """{"location":"Oslo","days":3}"""
let invalid = decodeText codec """{"location":"","days":0}"""
// InvalidValue errors at location and days
```

The `Days` codec reads an integer, applies both bounds, and wraps it. Encoding
unwraps it; the schema retains the integer shape and bounds. Constraints run
when decoding, so directly constructing `Days 0` bypasses them. Use smart
constructors if your application also needs to enforce rules on locally created
values.

## Registration is explicit

A static `JsonCodec` member is a convention for locating a type's codec. The
library does not discover it automatically. `register` returns a new immutable
registry; pass that registry to `autoWith` or `autoStrictWith`.

Plain `auto` has an empty registry. An unregistered wrapper DU is treated as a
structural union, which does not give it the scalar representation intended by
its custom codec. Single-case scalar wrappers therefore need registration.

The five built-in primitives (`string`, `int`, `int64`, `float`, `bool`) take
precedence over registry entries. Register a wrapper type to specialize one of
them. Registered codecs for dates, GUIDs, decimals, and other non-primitive
types take precedence over their built-in structural handling.

## Constraint combinators

| Combinator | Codec input | Schema contribution |
| --- | --- | --- |
| `gt`, `ge`, `lt`, `le` | Numeric constraints with compatible thresholds | `exclusiveMinimum`, `minimum`, `exclusiveMaximum`, `maximum` |
| `minLength`, `maxLength`, `nonEmpty` | `IJsonCodec<string>` | `minLength`, `maxLength`, `minLength: 1` |
| `pattern` | `IJsonCodec<string>` | `pattern` |
| `refine` | Any `IJsonCodec<'T>` | No automatic keyword for an arbitrary function |
| `map` | Any `IJsonCodec<'T>` with conversion functions | Preserves the source schema |
| `describe` | Any `IJsonCodec<'T>` | `description` |

Add `^` and `$` to a regex when the whole string must match. A pipeline such as
`Codec.int |> Codec.gt 0` creates a codec; it only affects a model when used by a
registered field type. You can also use its `Decode` method directly:

```fsharp
let dayCount = Codec.int |> Codec.gt 0 |> Codec.le 14
let checkedCount = dayCount.Decode (JInt 3)
// Ok 3
```

## Bundled refined types

Use `registerAll` from `Fable.TypedJson.Refined`, or register individual
`JsonCodec` members:

| Type | Rule |
| --- | --- |
| `NonEmptyString` | At least one character; whitespace is allowed |
| `PositiveInt` | Greater than zero |
| `NonNegativeInt` | Greater than or equal to zero |
| `Email` | Pragmatic email-pattern match |
| `Url` | `http://` or `https://` prefix with a non-empty suffix |
| `Uuid` | UUID-pattern match |

These are focused checks. `Email` does not implement the full email-address
specification, and `Url` does not validate every URI component. Their companion
modules expose `value`; `NonEmptyString`, `PositiveInt`, and `NonNegativeInt`
also expose `tryCreate`.

## Cross-field rules

Use `withModel` for a rule that needs the decoded record:

```fsharp
type Range = { Start: int; Until: int }

let rangeCodec =
    auto<Range> ()
    |> alias "Until" "end"
    |> withModel (fun range ->
        if range.Start <= range.Until then
            Ok range
        else
            Error [ { path = ""; message = "start must precede end" } ])

let invalidRange = decodeText rangeCodec """{"start":5,"end":2}"""
```

The validator runs only after field decoding succeeds, on every decode path,
including string maps and case-rule overrides. An empty path denotes a
model-level error; use a field path when a specific field should receive it.
It may return a transformed record. It does not run on encoding and does not
add JSON Schema constraints.

## Fully custom representations

Implement `IJsonCodec<'T>` or use `Codec.mk` when a primitive pipeline cannot
describe the representation. For example, a domain union can use a Boolean wire
value instead of the default tagged object:

```fsharp
type Switch = Enabled | Disabled

let switchCodec: IJsonCodec<Switch> =
    Codec.mk
        (function
        | JBool true -> Ok Enabled
        | JBool false -> Ok Disabled
        | _ -> Error "expected a Boolean switch")
        (function Enabled -> JBool true | Disabled -> JBool false)
        (primitiveSchema "boolean")

type Device = { Power: Switch }

let deviceRegistry = emptyRegistry |> register switchCodec
let deviceCodec = autoWith<Device> deviceRegistry
let device = decodeText deviceCodec """{"power":true}"""
// Ok { Power = Enabled }
```

The interface members are `Decode: JsonValue -> Result<'T, string>`,
`Encode: 'T -> JsonValue`, and `Schema: JsonSchema`. Keep all three consistent
and register the codec before constructing models that use it. Custom decode
errors acquire the containing field's path.
