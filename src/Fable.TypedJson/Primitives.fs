(**
# Primitives — the one JSON ↔ F# primitive coercion table

`Schema.coerce` and the `Codec.int` / `int64` / `float` / `string` / `bool`
values implement the same coercion rules for the same five types, but reach
them from different directions: `coerce` dispatches on a backend-native value
via `IJsonBackend.IsX` / `AsX`, while a codec pattern-matches a `JsonValue`.
Written twice, they drifted — `Codec.float` lost the InvariantCulture pin,
`Codec.int64` wrapped past Int32 range, and the two rendered floats to strings
differently.

What actually differs between the two callers is only *how the source value is
classified*. The conversion itself is a pure function on primitives, with no
backend and no `JsonValue` in sight. That is what lives here, so both callers
share one definition instead of agreeing by convention.

principle: conversion rules are pure functions on primitives — classification belongs to the caller
adr: one module ahead of both `Schema` and `Codec` in compile order, so neither can fork the rules
invariant: `Schema.coerce` and the matching `Codec` primitive produce the same result for the same input
*)

module Fable.TypedJson.Primitives

// ============================================================================
// Parsing — string → primitive
// ============================================================================

/// Cross-type coercion is intentional and load-bearing: LLM tool calls deliver
/// every argument as a string, so `"42"` must reach an `int` field.
let parseInt (s: string) : Result<int, string> =
    match System.Int32.TryParse(s) with
    | true, n -> Ok n
    | _ -> Error(sprintf "cannot parse '%s' as int" s)

let parseInt64 (s: string) : Result<int64, string> =
    match System.Int64.TryParse(s) with
    | true, n -> Ok n
    | _ -> Error(sprintf "cannot parse '%s' as int64" s)

(**
JSON numbers are always `.`-as-decimal per RFC 8259. On the CLR the
parameterless `TryParse` reads the *thread* culture, which on a
`.`-as-thousands locale (es/fr/de/…) silently turns `"22.5"` into `225` —
pin InvariantCulture there.

Fable backends transpile to locale-immune native parsers (Erlang
`binary_to_float`, Python `float`, JS `parseFloat`) and do not implement the
3-argument overload — it returns `0.0` on BEAM — so the short form is the
correct one there, not merely the convenient one.

adr: the culture pin lives here alone; both callers used to carry their own copy and one of them lost it
*)
let parseFloat (s: string) : Result<float, string> =
#if FABLE_COMPILER
    match System.Double.TryParse(s) with
#else
    match System.Double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
#endif
    | true, f -> Ok f
    | _ -> Error(sprintf "cannot parse '%s' as float" s)

/// Accepts only the two JSON literals, case-insensitively. Deliberately not
/// `"1"` / `"yes"` / `""` — a silent truthiness rule is the wrong default for
/// validating tool input.
let parseBool (s: string) : Result<bool, string> =
    match s.ToLower() with
    | "true" -> Ok true
    | "false" -> Ok false
    | _ -> Error(sprintf "cannot parse '%s' as bool" s)

(**
JSON has no date, uuid or exact-decimal type, so all three cross the wire as
strings. ISO-8601 and canonical uuid are what `format: date-time` and
`format: uuid` name, and are what every consumer expects.

adr: parse leniently, render strictly — decode takes whatever the runtime's
     parser accepts, encode always emits the round-trippable canonical form
*)
let parseDateTime (s: string) : Result<System.DateTime, string> =
    match System.DateTime.TryParse(s) with
    // Normalised to UTC on the way in as well as out, so a decoded value has a
    // known `Kind` regardless of what the backend's parser chose. .NET's
    // `Parse` yields `Local` for an offset-bearing string and `Unspecified`
    // for a bare one; Python yields aware and naive respectively.
    | true, d -> Ok(d.ToUniversalTime())
    | _ -> Error(sprintf "cannot parse '%s' as DateTime" s)

/// `Guid.Parse` guarded, not `TryParse`: the latter's `byref` overload is not
/// uniformly available across the Fable backends, and a guarded `Parse` is.
let parseGuid (s: string) : Result<System.Guid, string> =
    try
        Ok(System.Guid.Parse s)
    with _ ->
        Error(sprintf "cannot parse '%s' as Guid" s)

(**
Same InvariantCulture reasoning as `parseFloat`: the parameterless CLR overload
reads the thread culture, and the 3-argument form is not implemented on the
Fable backends.
*)
let parseDecimal (s: string) : Result<decimal, string> =
#if FABLE_COMPILER
    match System.Decimal.TryParse(s) with
#else
    match System.Decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture) with
#endif
    | true, d -> Ok d
    | _ -> Error(sprintf "cannot parse '%s' as decimal" s)

// ============================================================================
// Rendering — primitive → string
// ============================================================================

let intToString (n: int) : string = string n

let int64ToString (n: int64) : string = string n

(**
No culture guard needed, unlike `parseFloat`. F#'s `string` operator is
already invariant for primitives — it is not `x.ToString()`, which would read
the thread culture and emit `"3,14"` under de-DE. Verified against a de-DE
`CurrentCulture` in `TestCodec`, which fails on an unpinned *parse* and passes
on this render.

Not `sprintf "%f"`, which pads to a fixed six decimals — that is what made
this disagree with `Schema.coerce`'s string arm before the two were merged.

invariant: `parseFloat (floatToString f) = Ok f` regardless of ambient culture
*)
let floatToString (f: float) : string = string f

/// The JSON literals, not .NET's `"True"` / `"False"`.
let boolToString (b: bool) : string = if b then "true" else "false"

(**
The wire carries an *instant*: always UTC, always with the `Z` designator. That
is RFC 3339, which is what JSON Schema's `format: date-time` denotes.

Normalising rather than rendering the value's own `DateTimeKind` is deliberate
and load-bearing. `ToString("O")` on an `Unspecified` DateTime appends the
machine's local offset on Fable Python and nothing on .NET, so "render the kind
as-is" produces a *different string per backend for the same value* — precisely
the drift this library exists to prevent.

The `Z` is concatenated rather than written into the format string: `'Z'` as a
quoted literal is a portability question across four format-string
implementations, and this is not.

tradeoff: `Kind` does not survive the round trip — a decoded value is always
          `Utc`, and an `Unspecified` one is read as local time on the way out,
          per .NET's own `ToUniversalTime` semantics
*)
let dateTimeToString (d: System.DateTime) : string =
    d.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff")
    + "Z"

/// Canonical 8-4-4-4-12, lower case — what `format: uuid` denotes.
let guidToString (g: System.Guid) : string = g.ToString()

(**
Rendered as a string rather than a JSON number, deliberately. A decimal exists
precisely because binary floating point cannot represent it, so emitting one
through the backend's number type would discard the guarantee the type was
chosen for. Pydantic serializes `Decimal` to a string in JSON mode for the same
reason.

tradeoff: consumers see `"12.34"`, not `12.34` — the schema says so
          (`format: decimal`), and decode still accepts a bare JSON number
*)
let decimalToString (d: decimal) : string = string d
