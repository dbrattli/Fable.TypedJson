module Fable.TypedJson.Optimizations.Numbers

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
let inline tryParseFloat (s: string) : bool * float =
#if FABLE_COMPILER
    System.Double.TryParse(s)
#else
    System.Double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)
#endif

/// Use the native Fable parser or the culture-independent CLR overload.
let inline tryParseDecimal (s: string) : bool * decimal =
#if FABLE_COMPILER
    System.Decimal.TryParse(s)
#else
    System.Decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture)
#endif
