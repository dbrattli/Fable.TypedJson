(**
# Casing — the canonical-PascalCase pivot, shared by Schema and Json

Two layers need to turn a reflection-supplied field name into a JSON key, and
they must agree: `Schema.lowerFirstTransform` (backing `dump` / `validateJson` /
`validateMap`) and `Json.applyCaseRule` (backing the `CaseRules` codec API).
`Json` compiles after `Schema`, so the pivot they share lives here, ahead of
both.

The pivot exists because `PropertyInfo.Name` has not always reported the F#
spelling. BEAM reported snake_case before Fable 5.8.1
([Fable#4766](https://github.com/fable-compiler/Fable/pull/4766)) and Python
before 5.14.0 ([Fable#4852](https://github.com/fable-compiler/Fable/pull/4852)).
Normalizing to PascalCase first means a JSON key is derived from the F# field
name, not from whichever spelling the compiler happened to hand us — so the same
record produces the same wire format on every target and across that upgrade.

principle: a JSON key is a function of the F# field name alone — never of the backend or the Fable version
invariant: `toCanonicalPascal` is idempotent — applying it to its own output is a no-op
adr: uppercase-presence is the snake_case test — reflection never returns a mixed form, so there is no third case to distinguish
assumption: a name with no uppercase letter is snake_case (or a genuinely lowercase F# field, which round-trips unchanged)
*)

module Fable.TypedJson.Casing

/// Lowercase the first character; leave the rest alone.
let lowerFirst (name: string) : string =
    if name.Length = 0 then
        name
    else
        string (System.Char.ToLowerInvariant name.[0])
        + name.[1..]

/// Convert a snake_case name back to PascalCase.
let fromSnakeCase (name: string) : string =
    name.Split '_'
    |> Array.map (fun part ->
        if part.Length = 0 then
            part
        else
            string (System.Char.ToUpperInvariant part.[0])
            + part.[1..])
    |> String.concat ""

/// True if the name contains any uppercase letter (i.e., looks like Pascal/camelCase
/// rather than snake_case). Used to decide whether to convert before applying a rule.
let hasUpper (name: string) : bool = String.exists System.Char.IsUpper name

(**
Normalize a reflection-supplied field name to the canonical PascalCase form
every key transform pivots through.

The snake_case → PascalCase direction is lossy where the F# name had adjacent
capitals: Python < 5.14.0 reported `HTTPStatus` as `httpstatus`, which comes
back as `Httpstatus`. Fable#4852 is the only real fix for those; this recovers
the ordinary `AirTemperature` case, which is what the key transforms need.

tradeoff: adjacent-capital names are not fully recoverable from snake_case — accepted, the alternative is a per-target key format
*)
let toCanonicalPascal (name: string) : string =
    if hasUpper name then name else fromSnakeCase name
