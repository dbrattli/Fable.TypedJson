(**
# Codec — Primitive codecs and validator combinators

Provides `IJsonCodec<'T>` values for built-in primitives plus a small
combinator module for stacking validators on top, à la Pydantic's
`Annotated[int, Field(gt=0, lt=15)]`.

Composition reads left-to-right via `|>`:

```
Codec.int |> Codec.gt 0 |> Codec.le 14
```

Each combinator takes an `IJsonCodec<'T>` and returns an `IJsonCodec<'T>`
with one extra validation step layered on the `Decode` path AND the
matching JSON Schema constraint key threaded into `Schema`. Errors are
returned as `Result<'T, string>`; the caller (Schema.coerce) surfaces
them into a `FieldError`.
*)

module Fable.TypedJson.Codec

open System.Text.RegularExpressions

open Fable.TypedJson.Schema

// ============================================================================
// Constructor helpers
// ============================================================================

let mk
    (decode: JsonValue -> Result<'T, string>)
    (encode: 'T -> JsonValue)
    (schema: JsonSchema)
    : IJsonCodec<'T> =
    { new IJsonCodec<'T> with
        member _.Decode jv = decode jv
        member _.Encode v = encode v
        member _.Schema = schema
    }

let chain (validate: 'T -> Result<'T, string>) (inner: IJsonCodec<'T>) : IJsonCodec<'T> =
    mk
        (fun jv ->
            match inner.Decode jv with
            | Ok v -> validate v
            | Error e -> Error e)
        (fun v -> inner.Encode v)
        inner.Schema

/// Add or overwrite a single key in a codec's JSON Schema fragment.
let withSchemaKey (key: string) (value: JsonSchemaValue) (codec: IJsonCodec<'T>) : IJsonCodec<'T> =
    mk
        (fun jv -> codec.Decode jv)
        (fun v -> codec.Encode v)
        (Map.add key value codec.Schema)

// ============================================================================
// Primitive codecs
// ============================================================================

let int: IJsonCodec<int> =
    mk
        (fun jv ->
            match jv with
            | JInt n -> Ok n
            | JFloat f -> Ok(int f)
            | JString s ->
                match System.Int32.TryParse s with
                | true, n -> Ok n
                | _ -> Error(sprintf "cannot parse '%s' as int" s)
            | _ -> Error "expected int")
        (fun n -> JInt n)
        (primitiveSchema "integer")

let int64: IJsonCodec<int64> =
    mk
        (fun jv ->
            match jv with
            | JInt n -> Ok(Microsoft.FSharp.Core.Operators.int64 n)
            | JFloat f -> Ok(Microsoft.FSharp.Core.Operators.int64 f)
            | JString s ->
                match System.Int64.TryParse s with
                | true, n -> Ok n
                | _ -> Error(sprintf "cannot parse '%s' as int64" s)
            | _ -> Error "expected int64")
        (fun n -> JInt(Microsoft.FSharp.Core.Operators.int n))
        (primitiveSchema "integer")

let float: IJsonCodec<float> =
    mk
        (fun jv ->
            match jv with
            | JFloat f -> Ok f
            | JInt n -> Ok(float n)
            | JString s ->
                match System.Double.TryParse s with
                | true, f -> Ok f
                | _ -> Error(sprintf "cannot parse '%s' as float" s)
            | _ -> Error "expected float")
        (fun f -> JFloat f)
        (primitiveSchema "number")

let string: IJsonCodec<string> =
    mk
        (fun jv ->
            match jv with
            | JString s -> Ok s
            | JInt n -> Ok(sprintf "%d" n)
            | JFloat f -> Ok(sprintf "%f" f)
            | JBool b -> Ok(if b then "true" else "false")
            | _ -> Error "expected string")
        (fun s -> JString s)
        (primitiveSchema "string")

let bool: IJsonCodec<bool> =
    mk
        (fun jv ->
            match jv with
            | JBool b -> Ok b
            | JString s ->
                match s.ToLower() with
                | "true" -> Ok true
                | "false" -> Ok false
                | _ -> Error(sprintf "cannot parse '%s' as bool" s)
            | _ -> Error "expected bool")
        (fun b -> JBool b)
        (primitiveSchema "boolean")

// ============================================================================
// Combinators — stack validators on a base codec, AND record the matching
// JSON Schema constraint keyword on the way through.
// ============================================================================

/// Greater-than constraint. `Codec.int |> Codec.gt 0` rejects values ≤ 0.
/// JSON Schema: `exclusiveMinimum`.
let inline gt (threshold: ^a) (codec: IJsonCodec< ^a >) : IJsonCodec< ^a > when ^a: comparison =
    chain (fun v -> if v > threshold then Ok v else Error(sprintf "must be > %A" threshold)) codec
    |> withSchemaKey "exclusiveMinimum" (SVFloat(Operators.float threshold))

/// Greater-than-or-equal constraint. JSON Schema: `minimum`.
let inline ge (threshold: ^a) (codec: IJsonCodec< ^a >) : IJsonCodec< ^a > when ^a: comparison =
    chain (fun v -> if v >= threshold then Ok v else Error(sprintf "must be >= %A" threshold)) codec
    |> withSchemaKey "minimum" (SVFloat(Operators.float threshold))

/// Less-than constraint. JSON Schema: `exclusiveMaximum`.
let inline lt (threshold: ^a) (codec: IJsonCodec< ^a >) : IJsonCodec< ^a > when ^a: comparison =
    chain (fun v -> if v < threshold then Ok v else Error(sprintf "must be < %A" threshold)) codec
    |> withSchemaKey "exclusiveMaximum" (SVFloat(Microsoft.FSharp.Core.Operators.float threshold))

/// Less-than-or-equal constraint. JSON Schema: `maximum`.
let inline le (threshold: ^a) (codec: IJsonCodec< ^a >) : IJsonCodec< ^a > when ^a: comparison =
    chain (fun v -> if v <= threshold then Ok v else Error(sprintf "must be <= %A" threshold)) codec
    |> withSchemaKey "maximum" (SVFloat(Microsoft.FSharp.Core.Operators.float threshold))

/// String minimum length constraint. JSON Schema: `minLength`.
let minLength (n: int) (codec: IJsonCodec<string>) : IJsonCodec<string> =
    chain
        (fun (s: string) ->
            if s.Length >= n then
                Ok s
            else
                Error(sprintf "must have length >= %d" n))
        codec
    |> withSchemaKey "minLength" (SVInt n)

/// String maximum length constraint. JSON Schema: `maxLength`.
let maxLength (n: int) (codec: IJsonCodec<string>) : IJsonCodec<string> =
    chain
        (fun (s: string) ->
            if s.Length <= n then
                Ok s
            else
                Error(sprintf "must have length <= %d" n))
        codec
    |> withSchemaKey "maxLength" (SVInt n)

/// String non-empty constraint (length ≥ 1). JSON Schema: `minLength: 1`.
let nonEmpty (codec: IJsonCodec<string>) : IJsonCodec<string> =
    chain (fun (s: string) -> if s.Length > 0 then Ok s else Error "must be non-empty") codec
    |> withSchemaKey "minLength" (SVInt 1)

/// Regex pattern constraint (anchored full-match). JSON Schema: `pattern`.
let pattern (regex: string) (codec: IJsonCodec<string>) : IJsonCodec<string> =
    let re = Regex regex

    chain
        (fun (s: string) ->
            if re.IsMatch s then
                Ok s
            else
                Error(sprintf "must match pattern '%s'" regex))
        codec
    |> withSchemaKey "pattern" (SVStr regex)

/// Custom validator — escape hatch for arbitrary post-decode rules.
/// Doesn't add anything to the JSON Schema (the rule is opaque).
let refine (validate: 'T -> Result<'T, string>) (codec: IJsonCodec<'T>) : IJsonCodec<'T> = chain validate codec

/// Add a free-form description to a codec's schema. JSON Schema: `description`.
let describe (text: string) (codec: IJsonCodec<'T>) : IJsonCodec<'T> =
    codec |> withSchemaKey "description" (SVStr text)

/// Map a codec from `'A` to `'B` via a pair of conversions. Schema is
/// preserved (the JSON shape doesn't change, only the F# type does).
///   `Codec.int |> Codec.gt 0 |> Codec.map Days (fun (Days n) -> n)`
let map (toOuter: 'A -> 'B) (fromOuter: 'B -> 'A) (codec: IJsonCodec<'A>) : IJsonCodec<'B> =
    mk
        (fun jv ->
            match codec.Decode jv with
            | Ok v -> Ok(toOuter v)
            | Error e -> Error e)
        (fun v -> codec.Encode(fromOuter v))
        codec.Schema
