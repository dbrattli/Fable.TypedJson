(**
# Plan — resolve a type once, then run closures

The library is a staged compiler. Stage 1 walks `typeof<'T>` at
codec-construction time and emits a tree of `Plan` nodes; stage 2 runs the
closures those nodes hold. No `System.Type`, no `FullName` comparison and no
`FSharpType` call survives into stage 2, **at any depth**.

That last clause is the whole point. `Schema.buildRecordSchemaUntyped` already
pre-bakes the top-level record, but a nested record falls through to
`Schema.resolveRecord`, which calls `FSharpType.GetRecordFields` and re-derives
every key on *every* decode. Two implementations of one algorithm, with
divergent error strings, and reflection on the hot path for anything below
depth 0.

principle: reflection happens once per codec, never per value
adr: a tree of closures over an interpretable DU — one indirect call per node beats a tag dispatch plus a call
invariant: a node's `Decode` is total for its declared type — unsupported shapes are rejected at construction

## Errors

`Decode` returns `Result<obj, FieldError list>`, not `Result<obj, string>`.
The old split — `coerce` returning a string, `resolveRecord` returning a
`FieldError list`, and `coerceUnion` collapsing one into the other via
`formatErrors` — is why a nested failure surfaced as the flattened blob
`"address: city: missing field"` under path `address`. Container nodes now
prefix their children's paths, so the same failure reports path `address.city`.

## Cycle guard

`type Tree = { Children: Tree list }` has no base case, so an eagerly built
plan tree would not terminate — `auto<Tree> ()` would hang at construction,
which is worse than today's behaviour, where `coerce` recurses lazily and runs
out of document. `Building` carries the root-to-node path of record/union
`FullName`s; re-entering a type already on it defers the sub-walk to call time.

adr: defer the sub-walk rather than tie a knot through a mutable ref cell — a
     captured `ref` is the one construct whose Fable BEAM lowering is unverified,
     and fable-library-beam's ref representation has already changed once (d9bf688)
tradeoff: a recursive type re-walks its own subtree per nested value — correct and
          stateless, and exactly what today's code does at every depth for every type
*)

module Fable.TypedJson.Plan

open FSharp.Reflection
open Fable.TypedJson.Backend
open Fable.TypedJson.Schema

// ============================================================================
// Node types
// ============================================================================

[<NoComparison; NoEquality>]
type Plan = {
    /// backend-native JSON value -> boxed F# value of this node's type
    Decode: obj -> Result<obj, FieldError list>
}

[<NoComparison; NoEquality>]
type FieldPlan = {
    /// wire key — case rule and alias already applied, resolved once
    Key: string
    Optional: bool
    /// inner type's `.Name`, for "missing field (expected X)". Error path only.
    TypeName: string
    /// plan for the INNER type, i.e. after any option unwrap
    Inner: Plan
    /// `id` for a required field, `buildOption innerType` for an optional one
    Wrap: obj -> obj
}

[<NoComparison; NoEquality>]
type RecordPlan = {
    Fields: FieldPlan[]
    Make: obj[] -> obj
    Title: string
}

[<NoComparison; NoEquality>]
type BuildCtx = {
    Backend: IJsonBackend
    Registry: CodecRegistry
    KeyTransform: string -> string
    TagTransform: string -> string
    /// root-to-node path of compound `FullName`s — see the cycle guard note
    Building: string list
}

// ============================================================================
// Error helpers
// ============================================================================

/// Join a parent path onto a child's. An index segment (`[0]`) binds directly
/// so a list element reads `members[1].city`, not `members.[1].city`.
let private joinPath (parent: string) (child: string) : string =
    if child = "" then parent
    elif parent = "" then child
    elif child.StartsWith "[" then parent + child
    else parent + "." + child

let private under (path: string) (errs: FieldError list) : FieldError list =
    errs
    |> List.map (fun e -> { e with path = joinPath path e.path })

/// A leaf failure, carrying no path of its own — the enclosing field or
/// element supplies it.
let private leafError (message: string) : Result<obj, FieldError list> =
    Error [ { path = ""; message = message } ]

// ============================================================================
// The walker
// ============================================================================

(**
Module-scope mutual recursion with explicit parameters. Never nested inside an
`inline` function — that is the Fable BEAM codegen bug where captured values
lower to `undefined`. `Schema.coerce` and `Json.transformValue` already use
this shape on all four targets, so it is proven rather than assumed.
*)
let rec forTypeIn (ctx: BuildCtx) (t: System.Type) : Plan =
    let fullName = t.FullName
    let b = ctx.Backend

    // Primitives first, matching `Schema.coerce`'s dispatch order — a codec
    // registered against `System.Int32` stays inert in both directions.
    if fullName = "System.String" then
        planString b
    elif fullName = "System.Int32" then
        planInt b
    elif fullName = "System.Int64" then
        planInt64 b
    elif fullName = "System.Double" then
        planFloat b
    elif fullName = "System.Boolean" then
        planBool b
    else
        match tryGetCodecEntry fullName ctx.Registry with
        // A user codec wins over compound dispatch. `toJsonValue` is the only
        // place a `JsonValue` is constructed — it exists solely to cross this
        // boundary.
        | Some entry ->
            let decode = entry.decode

            {
                Decode =
                    fun v ->
                        match decode (toJsonValue b v) with
                        | Ok x -> Ok x
                        | Error msg -> leafError msg
            }
        | None ->

            // F# list MUST precede the union test: `FSharpList<'T>` is itself a DU,
            // so `IsUnion` returns true for it on the CLR and would mis-route.
            if isFSharpListType fullName then
                planSeq ctx (getGenericInnerType t) buildList
            elif t.IsArray then
                planSeq ctx (t.GetElementType()) buildArray
            elif FSharpType.IsRecord t then
                if List.contains fullName ctx.Building then
                    planDeferred ctx t
                else
                    planRecord ctx t
            elif FSharpType.IsUnion t then
                if List.contains fullName ctx.Building then
                    planDeferred ctx t
                else
                    planUnion ctx t
            else
                let message = sprintf "cannot decode %s" fullName
                { Decode = fun _ -> leafError message }

// --- Primitive nodes --------------------------------------------------------
//
// Each closure knows its target type, so the `FullName` ladder `coerce` walks
// per value per decode is gone. Conversions come from `Primitives`, shared with
// `Schema.coerce`.

and private planString (b: IJsonBackend) : Plan = {
    Decode =
        fun v ->
            if b.IsString v then
                Ok(box (b.AsString v))
            elif b.IsInt v then
                Ok(box (Primitives.intToString (b.AsInt v)))
            elif b.IsFloat v then
                Ok(box (Primitives.floatToString (b.AsFloat v)))
            elif b.IsBool v then
                Ok(box (Primitives.boolToString (b.AsBool v)))
            else
                leafError (sprintf "cannot coerce %s to System.String" (describeValue b v))
}

and private planInt (b: IJsonBackend) : Plan = {
    Decode =
        fun v ->
            if b.IsInt v then
                Ok(box (b.AsInt v))
            elif b.IsFloat v then
                Ok(box (int (b.AsFloat v)))
            elif b.IsString v then
                match Primitives.parseInt (b.AsString v) with
                | Ok n -> Ok(box n)
                | Error msg -> leafError msg
            else
                leafError (sprintf "cannot coerce %s to System.Int32" (describeValue b v))
}

and private planInt64 (b: IJsonBackend) : Plan = {
    Decode =
        fun v ->
            if b.IsInt v then
                Ok(box (int64 (b.AsInt v)))
            elif b.IsFloat v then
                Ok(box (int64 (b.AsFloat v)))
            elif b.IsString v then
                match Primitives.parseInt64 (b.AsString v) with
                | Ok n -> Ok(box n)
                | Error msg -> leafError msg
            else
                leafError (sprintf "cannot coerce %s to System.Int64" (describeValue b v))
}

and private planFloat (b: IJsonBackend) : Plan = {
    Decode =
        fun v ->
            if b.IsFloat v then
                Ok(box (b.AsFloat v))
            elif b.IsInt v then
                Ok(box (float (b.AsInt v)))
            elif b.IsString v then
                match Primitives.parseFloat (b.AsString v) with
                | Ok f -> Ok(box f)
                | Error msg -> leafError msg
            else
                leafError (sprintf "cannot coerce %s to System.Double" (describeValue b v))
}

and private planBool (b: IJsonBackend) : Plan = {
    Decode =
        fun v ->
            if b.IsBool v then
                Ok(box (b.AsBool v))
            elif b.IsString v then
                match Primitives.parseBool (b.AsString v) with
                | Ok x -> Ok(box x)
                | Error msg -> leafError msg
            else
                leafError (sprintf "cannot coerce %s to System.Boolean" (describeValue b v))
}

// --- Sequences --------------------------------------------------------------

(**
`build` turns the collected elements into the target's runtime shape —
`buildList` for `'T list`, `buildArray` for `'T[]`. Both are needed because
CLR generics and arrays are invariant: an `obj list` cannot be assigned to an
`int list` field, nor an `obj[]` to an `int[]` one.

Iteration goes through `ArrayLength` / `ArrayAt` rather than an F# list or
array, because the native sequence shape differs per backend.

adr: fail-fast on the first bad element, unlike record fields which accumulate —
     an element index is rarely actionable in bulk and this keeps one traversal
*)
and private planSeq (ctx: BuildCtx) (elementType: System.Type) (build: System.Type -> obj list -> obj) : Plan =
    let b = ctx.Backend
    let element = forTypeIn ctx elementType
    let expected = sprintf "expected JSON array for %s[]" elementType.Name

    {
        Decode =
            fun v ->
                if not (b.IsArray v) then
                    leafError expected
                else
                    let len = b.ArrayLength v
                    let mutable i = 0
                    let mutable failure: FieldError list option = None
                    let mutable acc: obj list = []

                    while i < len && failure.IsNone do
                        match element.Decode(b.ArrayAt(v, i)) with
                        | Ok x -> acc <- x :: acc
                        | Error errs -> failure <- Some(under (sprintf "[%d]" i) errs)

                        i <- i + 1

                    match failure with
                    | Some errs -> Error errs
                    | None -> Ok(build elementType (List.rev acc))
    }

// --- Records ----------------------------------------------------------------

and private buildRecordPlan (ctx: BuildCtx) (t: System.Type) : RecordPlan =
    // The record joins the path before its fields are walked, so a field
    // referring straight back to it defers instead of descending.
    let inner = {
        ctx with
            Building = t.FullName :: ctx.Building
    }

    let fields =
        FSharpType.GetRecordFields t
        |> Array.map (fun fi ->
            let isOpt = isOptionType fi.PropertyType.FullName

            let innerType =
                if isOpt then
                    getGenericInnerType fi.PropertyType
                else
                    fi.PropertyType

            {
                Key = ctx.KeyTransform fi.Name
                Optional = isOpt
                TypeName = innerType.Name
                Inner = forTypeIn inner innerType
                Wrap = if isOpt then buildOption innerType else id
            })

    {
        Fields = fields
        Make = fun values -> FSharpValue.MakeRecord(t, values)
        Title = t.Name
    }

/// Stage 2 for a record. Every key, option test and type name was resolved at
/// construction; this indexes arrays and calls closures.
///
/// invariant: all fields are attempted and errors accumulate — not fail-fast
and decodeRecordWith (b: IJsonBackend) (rp: RecordPlan) (lookup: string -> obj option) : Result<obj, FieldError list> =
    let n = rp.Fields.Length
    let values = Array.zeroCreate<obj> n
    let mutable errs: FieldError list = []

    for i = 0 to n - 1 do
        let f = rp.Fields.[i]

        match lookup f.Key with
        | None ->
            if f.Optional then
                values.[i] <- box None
            else
                errs <-
                    {
                        path = f.Key
                        message = sprintf "missing field (expected %s)" f.TypeName
                    }
                    :: errs
        | Some raw when b.IsNull raw ->
            if f.Optional then
                values.[i] <- box None
            else
                errs <-
                    {
                        path = f.Key
                        message = sprintf "null where %s was required" f.TypeName
                    }
                    :: errs
        | Some raw ->
            match f.Inner.Decode raw with
            | Ok x -> values.[i] <- f.Wrap x
            | Error es -> errs <- List.rev (under f.Key es) @ errs

    if List.isEmpty errs then
        Ok(rp.Make values)
    else
        Error(List.rev errs)

and private planRecord (ctx: BuildCtx) (t: System.Type) : Plan =
    let b = ctx.Backend
    let rp = buildRecordPlan ctx t
    let expected = sprintf "expected JSON object for %s" rp.Title

    {
        Decode =
            fun v ->
                if b.IsMap v then
                    decodeRecordWith b rp (mapLookup b v)
                else
                    leafError expected
    }

// --- Unions -----------------------------------------------------------------

(**
A tagged DU is `{"type": "<tag>", ...payload}`. A fieldless case is just the
discriminator; a single record-payload case flattens the record's keys
alongside it, Pydantic style. Every other shape is rejected **here, at
construction** rather than per value — the caller learns when they build the
codec, not on the first document that happens to use that case.

invariant: an unsupported union shape fails at codec construction, never silently at run time
*)
and private buildCasePlans (ctx: BuildCtx) (t: System.Type) : Map<string, UnionCaseInfo * RecordPlan option> =
    let inner = {
        ctx with
            Building = t.FullName :: ctx.Building
    }

    FSharpType.GetUnionCases t
    |> Array.map (fun caseInfo ->
        let caseFields = caseInfo.GetFields()

        let payload =
            match caseFields.Length with
            | 0 -> None
            | 1 ->
                let payloadType = caseFields.[0].PropertyType

                if FSharpType.IsRecord payloadType then
                    Some(buildRecordPlan inner payloadType)
                else
                    failwithf
                        "union case %s has a non-record payload (%s); not supported in v1 — register an IJsonCodec for %s instead"
                        caseInfo.Name
                        payloadType.Name
                        t.Name
            | n -> failwithf "union case %s has %d positional fields; multi-field cases are not supported in v1" caseInfo.Name n

        ctx.TagTransform caseInfo.Name, (caseInfo, payload))
    |> Map.ofArray

and decodeUnionWith
    (b: IJsonBackend)
    (cases: Map<string, UnionCaseInfo * RecordPlan option>)
    (typeName: string)
    (lookup: string -> obj option)
    : Result<obj, FieldError list> =
    match lookup discriminatorKey with
    | None ->
        Error [
            {
                path = discriminatorKey
                message = sprintf "missing discriminator '%s' for %s" discriminatorKey typeName
            }
        ]
    | Some tagValue when b.IsString tagValue ->
        let tag = b.AsString tagValue

        match Map.tryFind tag cases with
        | None ->
            Error [
                {
                    path = discriminatorKey
                    message = sprintf "no case in %s matches discriminator value '%s'" typeName tag
                }
            ]
        | Some(caseInfo, None) -> Ok(FSharpValue.MakeUnion(caseInfo, [||]))
        | Some(caseInfo, Some rp) ->
            // The payload record shares the outer lookup — its keys sit
            // alongside the discriminator rather than nested under it.
            decodeRecordWith b rp lookup
            |> Result.map (fun payload -> FSharpValue.MakeUnion(caseInfo, [| payload |]))
    | Some _ ->
        Error [
            {
                path = discriminatorKey
                message = sprintf "discriminator '%s' must be a string" discriminatorKey
            }
        ]

and private planUnion (ctx: BuildCtx) (t: System.Type) : Plan =
    let b = ctx.Backend
    let cases = buildCasePlans ctx t
    let typeName = t.Name
    let expected = sprintf "expected JSON object for %s" typeName

    {
        Decode =
            fun v ->
                if b.IsMap v then
                    decodeUnionWith b cases typeName (mapLookup b v)
                else
                    leafError expected
    }

// --- Cycle guard ------------------------------------------------------------

and private planDeferred (ctx: BuildCtx) (t: System.Type) : Plan =
    // Restart with an empty path so the deferred walk expands one full level
    // before deferring again. Termination comes from the document running out
    // of nesting, exactly as it did before plans existed.
    let restart = { ctx with Building = [] }

    {
        Decode = fun v -> (forTypeIn restart t).Decode v
    }

// ============================================================================
// Entry points
// ============================================================================

let forType
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (tagTransform: string -> string)
    (t: System.Type)
    : Plan =
    forTypeIn
        {
            Backend = backend
            Registry = registry
            KeyTransform = keyTransform
            TagTransform = tagTransform
            Building = []
        }
        t

(**
Decode from a `key -> value` lookup rather than a native map, for sources that
are not backend-native JSON — `Map<string, string>` tool-call input in
particular. Records and unions read their fields through a lookup anyway, so
this costs nothing; any other top-level type has no keys to read and is
rejected.
*)
let forTypeFromLookup
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (tagTransform: string -> string)
    (t: System.Type)
    : (string -> obj option) -> Result<obj, FieldError list> =
    let ctx = {
        Backend = backend
        Registry = registry
        KeyTransform = keyTransform
        TagTransform = tagTransform
        Building = []
    }

    if FSharpType.IsRecord t then
        let rp = buildRecordPlan ctx t
        fun lookup -> decodeRecordWith backend rp lookup
    elif FSharpType.IsUnion t then
        let cases = buildCasePlans ctx t
        let typeName = t.Name
        fun lookup -> decodeUnionWith backend cases typeName lookup
    else
        let err = [
            {
                path = ""
                message = sprintf "auto<%s> requires a record or discriminated-union type" t.Name
            }
        ]

        fun _ -> Error err
