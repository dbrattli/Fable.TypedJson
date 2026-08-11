(**
# Primitives — the one JSON ↔ F# primitive coercion table

`Plan`'s primitive nodes and the `Codec.int` / `int64` / `float` / `string` /
`bool` values implement the same coercion rules for the same five types, but
reach them from different directions: a plan node dispatches on a
backend-native value via `IJsonBackend.IsX` / `AsX`, while a codec
pattern-matches a `JsonValue`.

What actually differs between the two callers is only *how the source value is
classified*. The conversion itself is a pure function on primitives, with no
backend and no `JsonValue` in sight. That is what lives here, so both callers
share one definition instead of agreeing by convention.

decision: conversion rules are pure functions on primitives — classification belongs to the caller
decision: keeps conversions in a module compiled before `Codec` and `Plan` — neither caller can own a divergent copy
invariant: a `Plan` primitive node and the matching `Codec` produce the same result for the same input
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

decision: pins CLR float parsing here — both primitive entry paths inherit locale-independent JSON semantics
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

decision: parses accepted runtime forms but renders one canonical form — encoded values round-trip across targets
*)

(**
## Zone handling

The offset is applied here rather than handed to the runtime's parser, because
the runtimes disagree. Fable's BEAM parser rejects `2026-08-03T16:30:15+02:00`
outright, and .NET's `Parse` yields `Local` for an offset-bearing string and
`Unspecified` for a bare one — so "let the runtime sort it out" produces three
different answers for one document. Splitting the offset off and doing the
arithmetic makes the result identical everywhere, which is the whole point of
this library.

A timestamp with no zone at all is read as **UTC**, not local. `ToUniversalTime`
on an `Unspecified` DateTime applies the machine's own offset, so the same
document would decode to different instants depending on where it was parsed.
RFC 3339 — what `format: date-time` denotes — requires an offset anyway.

Both divergences are written up upstream:
`../Fable/BEAM-DATETIME-ZONE-OFFSET-PROMPT.md` (Beam rejects numeric offsets)
and `../Fable/PYTHON-DATETIME-ROUNDTRIP-FORMAT-PROMPT.md` (`"O"` attaches the
machine's local offset). If both land, `splitZoneOffset` can collapse back to a
plain `TryParse` and `dateTimeToString` back to `"O"`.

invariant: one document, one instant, on every backend and every machine
*)

/// Splits an ISO-8601 timestamp into its zone-less part and its offset in
/// minutes east of UTC. `None` when there is no numeric offset (a bare
/// timestamp, or one ending in `Z`).
let private splitZoneOffset (s: string) : (string * int) option =
    // Only look past the date, which carries its own `-` separators.
    let dateLength = 10

    if s.Length <= dateLength then
        None
    else
        let tail = s.Substring dateLength
        let plus = tail.IndexOf '+'
        let minus = tail.IndexOf '-'

        let signIndex =
            if plus >= 0 && minus >= 0 then min plus minus
            elif plus >= 0 then plus
            else minus

        if signIndex < 0 then
            None
        else
            let offset = tail.Substring signIndex
            let digits = offset.Substring(1).Replace(":", "")

            if digits.Length < 2 then
                None
            else
                let hours = int (digits.Substring(0, 2))

                let minutes =
                    if digits.Length >= 4 then
                        int (digits.Substring(2, 2))
                    else
                        0

                let sign = if offset.[0] = '-' then -1 else 1
                Some(s.Substring(0, dateLength + signIndex), sign * ((hours * 60) + minutes))

/// Parses the zone-less part of a timestamp, tagging the result UTC without
/// consulting the machine's timezone.
let private parseAsUtc (s: string) : Result<System.DateTime, string> =
    match System.DateTime.TryParse s with
    | true, d -> Ok(System.DateTime.SpecifyKind(d, System.DateTimeKind.Utc))
    | _ -> Error(sprintf "cannot parse '%s' as DateTime" s)

let parseDateTime (s: string) : Result<System.DateTime, string> =
    match splitZoneOffset s with
    | Some(local, offsetMinutes) ->
        parseAsUtc local
        |> Result.map (fun d -> d.AddMinutes(float -offsetMinutes))
    | None ->
        // `Z` means UTC, which is what `parseAsUtc` assumes anyway; strip it so
        // every backend's parser sees the same zone-less string.
        let bare =
            if s.EndsWith "Z" || s.EndsWith "z" then
                s.Substring(0, s.Length - 1)
            else
                s

        parseAsUtc bare
        |> Result.mapError (fun _ -> sprintf "cannot parse '%s' as DateTime" s)

/// A `DateTimeOffset` carries its own offset, so nothing has to be assumed about
/// a bare timestamp — the reason to prefer it over `DateTime` on a wire format.
/// Built from the UTC instant above rather than `DateTimeOffset.TryParse`, which
/// is not implemented consistently across the Fable backends.
let parseDateTimeOffset (s: string) : Result<System.DateTimeOffset, string> =
    parseDateTime s
    |> Result.map (fun utc -> System.DateTimeOffset(utc, System.TimeSpan.Zero))
    |> Result.mapError (fun _ -> sprintf "cannot parse '%s' as DateTimeOffset" s)


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

Not `sprintf "%f"`, which pads to a fixed six decimals and would change the
wire representation.

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

`"O"` would be the natural specifier here and is deliberately avoided — on Fable
Python it attaches the machine's local offset to an `Unspecified` value. Written
up in `../Fable/PYTHON-DATETIME-ROUNDTRIP-FORMAT-PROMPT.md`.

tradeoff: normalizes `DateTime.Kind` to UTC so one wire value represents the same instant on every target
*)
let dateTimeToString (d: System.DateTime) : string =
    d.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff")
    + "Z"

/// Rendered in UTC with the `Z` designator, exactly like `dateTimeToString` —
/// the offset is preserved as an instant rather than as a local wall clock.
let dateTimeOffsetToString (d: System.DateTimeOffset) : string =
    d.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff")
    + "Z"

/// Canonical 8-4-4-4-12, lower case — what `format: uuid` denotes.
let guidToString (g: System.Guid) : string = g.ToString()

(**
Rendered as a string rather than a JSON number, deliberately. A decimal exists
precisely because binary floating point cannot represent it, so emitting one
through the backend's number type would discard the guarantee the type was
chosen for. Pydantic serializes `Decimal` to a string in JSON mode for the same
reason.

Note that `format: decimal` is not a registered JSON Schema format — formats are
an extensible annotation vocabulary, so this is legal but no validator will act
on it. It is emitted for documentation value.

tradeoff: emits decimals as strings to preserve precision while still accepting bare JSON numbers on decode
*)
let decimalToString (d: decimal) : string = string d
