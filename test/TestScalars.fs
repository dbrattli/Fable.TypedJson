(**
# TestScalars — DateTime, Guid and decimal

JSON has no date, uuid or exact-decimal type, so all three cross the wire as
strings carrying a `format` keyword. These tests pin both faces on every
backend: that the value round-trips, and that the emitted schema describes the
string form `Encode` actually produces.

Dispatch for these three happens AFTER the codec registry, unlike the five
primitives, so a consumer can still override the representation with a
registered codec.

invariant: schema `format` and wire encoding agree — both come off the same plan node
*)

module Fable.TypedJson.Tests.Scalars

open System

open Fable.TypedJson.Testing
open Fable.TypedJson.Schema
open Fable.TypedJson.Json
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

#if PYTHON
open Fable.TypedJson.Python.Json

let backend = python
#else
#if JS
open Fable.TypedJson.JS.Json

let backend = js
#else
#if DOTNET
open Fable.TypedJson.DotNet.Json

let backend = dotnet
#else
open Fable.TypedJson.Beam.Json

let backend = beam
#endif
#endif
#endif

type Event = { Name: string; At: DateTime }

type Entity = { Id: Guid; Label: string }

type Invoice = { Total: decimal; Currency: string }

type Meeting = {
    Title: string
    StartsAt: DateTimeOffset
}

let private prop (v: JsonSchemaValue) (key: string) : JsonSchemaValue option =
    match v with
    | SVDict m -> Map.tryFind key m
    | _ -> Option.None

/// The `type` / `format` pair a scalar's schema node carries.
let private typeAndFormat (schema: JsonSchemaValue) (field: string) : (string * string) option =
    prop schema "properties"
    |> Option.bind (fun p -> prop p field)
    |> Option.bind (fun f ->
        match prop f "type", prop f "format" with
        | Some(SVStr t), Some(SVStr fmt) -> Some(t, fmt)
        | _ -> Option.None)

// ============================================================================
// DateTime
// ============================================================================

let private dateTimeTests =
    testList (
        "DateTime",
        [
            // UTC in, UTC out. `Kind` is normalised rather than preserved —
            // rendering an `Unspecified` value's kind as-is produced a
            // different string per backend, which is the drift the library
            // exists to prevent.
            test (
                "round-trips a UTC instant exactly",
                fun _ ->
                    let codec = auto<Event> ()

                    let original = {
                        Name = "launch"
                        At = DateTime(2026, 8, 3, 14, 30, 15, DateTimeKind.Utc)
                    }

                    match codec.decode (parseRaw (codec.encode original)) with
                    | Ok decoded ->
                        assertThat decoded.Name (isEqualTo "launch")
                        assertThat decoded.At (isEqualTo original.At)
                    | Error errs -> failwith (formatErrors errs)
            )

            test (
                "encodes with the Z designator, as format: date-time denotes",
                fun _ ->
                    let codec = auto<Event> ()

                    let encoded =
                        codec.encode {
                            Name = "launch"
                            At = DateTime(2026, 8, 3, 14, 30, 15, DateTimeKind.Utc)
                        }

                    assertThat (encoded.Contains "2026-08-03T14:30:15") isTrue
                    assertThat (encoded.Contains "Z") isTrue
            )

            test (
                "decodes an offset-bearing string to the same instant",
                fun _ ->
                    let codec = auto<Event> ()

                    match codec.decode (parseRaw """{"name":"launch","at":"2026-08-03T14:30:15Z"}""") with
                    | Ok decoded -> assertThat decoded.At (isEqualTo (DateTime(2026, 8, 3, 14, 30, 15, DateTimeKind.Utc)))
                    | Error errs -> failwith (formatErrors errs)
            )

            // A bare timestamp is read as UTC, not as local time. `ToUniversalTime`
            // on an `Unspecified` DateTime applies the machine's own offset, which
            // would make the same document decode to different instants depending
            // on where it was parsed.
            test (
                "a timestamp with no zone is read as UTC, not local",
                fun _ ->
                    let codec = auto<Event> ()

                    match codec.decode (parseRaw """{"name":"launch","at":"2026-08-03T14:30:15"}""") with
                    | Ok decoded ->
                        assertThat decoded.At (isEqualTo (DateTime(2026, 8, 3, 14, 30, 15, DateTimeKind.Utc)))
                        // Machine-independent: the same wall clock, tagged UTC.
                        assertThat (decoded.At.Hour) (isEqualTo 14)
                    | Error errs -> failwith (formatErrors errs)
            )

            test (
                "an offset-bearing timestamp is converted, not relabelled",
                fun _ ->
                    let codec = auto<Event> ()

                    match codec.decode (parseRaw """{"name":"launch","at":"2026-08-03T16:30:15+02:00"}""") with
                    | Ok decoded -> assertThat decoded.At (isEqualTo (DateTime(2026, 8, 3, 14, 30, 15, DateTimeKind.Utc)))
                    | Error errs -> failwith (formatErrors errs)
            )

            test (
                "rejects a value that is not a date",
                fun _ ->
                    let codec = auto<Event> ()

                    match codec.decode (parseRaw """{"name":"launch","at":"not a date"}""") with
                    | Ok _ -> failwith "expected a decode failure"
                    | Error errs -> assertThat (List.isEmpty errs) isFalse
            )

            test (
                "schema says string / date-time",
                fun _ ->
                    let schema = jsonSchemaValueOf<Event> emptyRegistry CaseRules.LowerFirst
                    assertThat (typeAndFormat schema "at") (isEqualTo (Some("string", "date-time")))
            )
        ]
    )

// ============================================================================
// Guid
// ============================================================================

let private guidTests =
    testList (
        "Guid",
        [
            test (
                "round-trips through its canonical form",
                fun _ ->
                    let codec = auto<Entity> ()

                    let original = {
                        Id = Guid.Parse "6f9619ff-8b86-d011-b42d-00c04fc964ff"
                        Label = "widget"
                    }

                    match codec.decode (parseRaw (codec.encode original)) with
                    | Ok decoded -> assertThat decoded.Id (isEqualTo original.Id)
                    | Error errs -> failwith (formatErrors errs)
            )

            test (
                "rejects a malformed uuid",
                fun _ ->
                    let codec = auto<Entity> ()

                    match codec.decode (parseRaw """{"id":"not-a-uuid","label":"x"}""") with
                    | Ok _ -> failwith "expected a decode failure"
                    | Error errs -> assertThat (List.isEmpty errs) isFalse
            )

            test (
                "schema says string / uuid",
                fun _ ->
                    let schema = jsonSchemaValueOf<Entity> emptyRegistry CaseRules.LowerFirst
                    assertThat (typeAndFormat schema "id") (isEqualTo (Some("string", "uuid")))
            )
        ]
    )

// ============================================================================
// decimal
// ============================================================================

let private decimalTests =
    testList (
        "decimal",
        [
            test (
                "round-trips without going through a float",
                fun _ ->
                    let codec = auto<Invoice> ()
                    let original = { Total = 12.34m; Currency = "NOK" }

                    match codec.decode (parseRaw (codec.encode original)) with
                    | Ok decoded -> assertThat decoded.Total (isEqualTo 12.34m)
                    | Error errs -> failwith (formatErrors errs)
            )

            // The reason the type exists: a value binary floating point cannot
            // hold must survive the round trip intact.
            test (
                "preserves precision a float would lose",
                fun _ ->
                    let codec = auto<Invoice> ()

                    let original = {
                        Total = 0.1m + 0.2m
                        Currency = "NOK"
                    }

                    match codec.decode (parseRaw (codec.encode original)) with
                    | Ok decoded -> assertThat decoded.Total (isEqualTo 0.3m)
                    | Error errs -> failwith (formatErrors errs)
            )

            test (
                "accepts a bare JSON number on the way in",
                fun _ ->
                    let codec = auto<Invoice> ()

                    match codec.decode (parseRaw """{"total":42,"currency":"NOK"}""") with
                    | Ok decoded -> assertThat decoded.Total (isEqualTo 42m)
                    | Error errs -> failwith (formatErrors errs)
            )

            test (
                "schema says string / decimal",
                fun _ ->
                    let schema = jsonSchemaValueOf<Invoice> emptyRegistry CaseRules.LowerFirst
                    assertThat (typeAndFormat schema "total") (isEqualTo (Some("string", "decimal")))
            )
        ]
    )

// ============================================================================
// DateTimeOffset
// ============================================================================

let private dateTimeOffsetTests =
    testList (
        "DateTimeOffset",
        [
            // The type that carries its own offset, so nothing has to be assumed
            // about a bare timestamp — the reason to prefer it on a wire format.
            test (
                "round-trips an offset as the same instant",
                fun _ ->
                    let codec = auto<Meeting> ()

                    let original = {
                        Title = "standup"
                        StartsAt = DateTimeOffset(2026, 8, 3, 16, 30, 15, TimeSpan.FromHours 2.0)
                    }

                    match codec.decode (parseRaw (codec.encode original)) with
                    // Compared as instants, explicitly. `DateTimeOffset` equality
                    // is instant-based on .NET but the Fable backends do not all
                    // agree, and the wire carries the instant either way.
                    | Ok decoded -> assertThat (decoded.StartsAt.UtcDateTime) (isEqualTo original.StartsAt.UtcDateTime)
                    | Error errs -> failwith (formatErrors errs)
            )

            test (
                "schema says string / date-time",
                fun _ ->
                    let schema = jsonSchemaValueOf<Meeting> emptyRegistry CaseRules.LowerFirst
                    assertThat (typeAndFormat schema "startsAt") (isEqualTo (Some("string", "date-time")))
            )
        ]
    )

// ============================================================================
// Registry override
// ============================================================================

(**
The three scalars dispatch AFTER the codec registry, unlike the five primitives.
That ordering is the whole reason a consumer can change how a date or a decimal
is represented, so it is pinned rather than left to the comment in `Plan.fs`.
*)

let private overrideTests =
    testList (
        "Registry override",
        [
            test (
                "a registered codec wins over the built-in scalar node",
                fun _ ->
                    // Renders a Guid as its braced form instead of the canonical one.
                    let bracedGuid: IJsonCodec<Guid> =
                        Fable.TypedJson.Codec.mk
                            (fun v ->
                                match v with
                                | JString s -> Ok(Guid.Parse(s.Trim('{', '}')))
                                | _ -> Error "expected a string")
                            (fun (g: Guid) -> JString("{" + string g + "}"))
                            (Map.ofList [ "type", SVStr "string"; "format", SVStr "braced-uuid" ])

                    let registry = emptyRegistry |> register bracedGuid

                    let codec = autoWith<Entity> registry

                    let encoded =
                        codec.encode {
                            Id = Guid.Parse "6f9619ff-8b86-d011-b42d-00c04fc964ff"
                            Label = "widget"
                        }

                    assertThat (encoded.Contains "{6f9619ff") isTrue

                    // The schema follows the codec too, not the built-in node.
                    let schema = jsonSchemaValueOf<Entity> registry CaseRules.LowerFirst
                    assertThat (typeAndFormat schema "id") (isEqualTo (Some("string", "braced-uuid")))
            )
        ]
    )

let tests =
    testList ("Scalars", [ dateTimeTests; guidTests; decimalTests; dateTimeOffsetTests; overrideTests ])
