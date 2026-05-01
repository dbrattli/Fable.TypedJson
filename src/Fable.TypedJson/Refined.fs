(**
# Refined — Bundled refined types (Pydantic-equivalent)

Single-case DUs with smart constructors and a static `JsonCodec` member.
Each is the `validators-as-types` pattern instantiated for a common
constraint:

```
NonEmptyString → string  with length >= 1
PositiveInt    → int     with > 0
NonNegativeInt → int     with >= 0
Email          → string  matching an email pattern
Url            → string  starting with http:// or https://
Uuid           → string  matching a UUID pattern
```

Use directly as record fields:

```fsharp
type Account = { Email: Email; Username: NonEmptyString }
let codecs = emptyRegistry |> register Email.JsonCodec |> register NonEmptyString.JsonCodec
let codec = autoWith<Account> codecs
```

User-defined refined types follow the same template — see `DayCount` in
the test suite.
*)

module Fable.TypedJson.Refined

open Fable.TypedJson.Schema

// ============================================================================
// NonEmptyString
// ============================================================================

type NonEmptyString =
    | NonEmptyString of string

    static member JsonCodec: IJsonCodec<NonEmptyString> =
        Codec.string
        |> Codec.nonEmpty
        |> Codec.map NonEmptyString (fun (NonEmptyString s) -> s)

module NonEmptyString =
    let value (NonEmptyString s) = s

    let tryCreate s =
        if System.String.IsNullOrEmpty s then
            Error "must be non-empty"
        else
            Ok(NonEmptyString s)

// ============================================================================
// PositiveInt — integers > 0
// ============================================================================

type PositiveInt =
    | PositiveInt of int

    static member JsonCodec: IJsonCodec<PositiveInt> =
        Codec.int
        |> Codec.gt 0
        |> Codec.map PositiveInt (fun (PositiveInt n) -> n)

module PositiveInt =
    let value (PositiveInt n) = n

    let tryCreate n =
        if n > 0 then
            Ok(PositiveInt n)
        else
            Error(sprintf "must be > 0, got %d" n)

// ============================================================================
// NonNegativeInt — integers >= 0
// ============================================================================

type NonNegativeInt =
    | NonNegativeInt of int

    static member JsonCodec: IJsonCodec<NonNegativeInt> =
        Codec.int
        |> Codec.ge 0
        |> Codec.map NonNegativeInt (fun (NonNegativeInt n) -> n)

module NonNegativeInt =
    let value (NonNegativeInt n) = n

    let tryCreate n =
        if n >= 0 then
            Ok(NonNegativeInt n)
        else
            Error(sprintf "must be >= 0, got %d" n)

// ============================================================================
// Email — strings matching a pragmatic email pattern
// ============================================================================

// Pragmatic pattern: not RFC 5322 strict, but catches typos.
// Anchored full-match by Codec.pattern (we add explicit anchors).
[<Literal>]
let private emailPattern = "^[^\s@]+@[^\s@]+\.[^\s@]+$"

type Email =
    | Email of string

    static member JsonCodec: IJsonCodec<Email> =
        Codec.string
        |> Codec.pattern emailPattern
        |> Codec.map Email (fun (Email s) -> s)

module Email =
    let value (Email s) = s

// ============================================================================
// Url — strings starting with http:// or https://
// ============================================================================

[<Literal>]
let private urlPattern = "^https?://.+"

type Url =
    | Url of string

    static member JsonCodec: IJsonCodec<Url> =
        Codec.string
        |> Codec.pattern urlPattern
        |> Codec.map Url (fun (Url s) -> s)

module Url =
    let value (Url s) = s

// ============================================================================
// Uuid — UUID v4-style pattern (8-4-4-4-12 hex digits)
// ============================================================================

[<Literal>]
let private uuidPattern = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"

type Uuid =
    | Uuid of string

    static member JsonCodec: IJsonCodec<Uuid> =
        Codec.string
        |> Codec.pattern uuidPattern
        |> Codec.map Uuid (fun (Uuid s) -> s)

module Uuid =
    let value (Uuid s) = s

// ============================================================================
// Convenience: register all bundled refined types in one call
// ============================================================================

/// Register every bundled refined type's codec in the supplied registry.
/// Useful at app startup: `let codecs = emptyRegistry |> Refined.registerAll`.
let registerAll (registry: CodecRegistry) : CodecRegistry =
    registry
    |> register NonEmptyString.JsonCodec
    |> register PositiveInt.JsonCodec
    |> register NonNegativeInt.JsonCodec
    |> register Email.JsonCodec
    |> register Url.JsonCodec
    |> register Uuid.JsonCodec
