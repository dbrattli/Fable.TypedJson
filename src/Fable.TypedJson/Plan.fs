(**
# Plan — resolve a type once, then run closures

The library is a staged compiler. Stage 1 walks `typeof<'T>` at
codec-construction time and emits a tree of `Plan` nodes; stage 2 runs the
closures those nodes hold. No `System.Type`, no `FullName` comparison and no
`FSharpType` call survives into stage 2, **at any depth**.

The same walk emits decode, encode, and JSON Schema data. Keeping all three
faces on one node prevents wire-shape drift while keeping reflection out of
per-value work at every depth.

decision: resolves reflection once per codec — repeated decode and encode calls stay on precomputed closures
decision: emits a tree of closures rather than an interpreted plan DU — avoids a tag dispatch before each node call
invariant: a node's `Decode` is total for its declared type — unsupported shapes are rejected at construction

## Errors

`Decode` returns `Result<obj, FieldError list>`. Container nodes prefix their
children's paths, so a nested failure retains the actionable path
`address.city` instead of flattening context into an error message.

## Cycle guard

`type Tree = { Children: Tree list }` has no type-level base case, so an eagerly
built plan tree would not terminate. `Building` carries the root-to-node path
of record/union `FullName`s; re-entering a type already on it defers the
sub-walk to call time, where the document supplies the finite base case.

decision: defers recursive sub-walks instead of tying mutable-ref knots — captured-ref lowering is unverified on BEAM
tradeoff: re-walks a recursive subtree per nested value to keep recursive planning stateless and portable
*)

module internal Fable.TypedJson.Plan

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
    /// boxed F# value of this node's type -> backend-native JSON value
    Encode: obj -> obj
    /// JSON Schema fragment describing what this node accepts. Produced by the
    /// SAME walk as Decode/Encode, which is why the emitter can no longer lack
    /// a case the other two handle — `JsonSchemaGen` had no union branch.
    Schema: JsonSchemaValue
    /// Named schemas hoisted out of this subtree. Always empty in the default
    /// flat mode; in `$ref` mode (`BuildCtx.RefMode`) every compound node puts
    /// its own body here and leaves a `$ref` behind in `Schema`.
    ///
    /// Threaded up through the return rather than accumulated in a mutable cell:
    /// BEAM has no module-level mutable state, and a captured `ref` is the one
    /// construct whose Fable BEAM lowering this file already avoids.
    Definitions: Map<string, JsonSchemaValue>
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
    /// Read this field off a boxed record. Per-field `GetRecordField`, never
    /// `GetRecordFields(box record)` — Fable BEAM lowers a boxed record's type
    /// to `System.Object`, so the whole-record form cannot recover the fields.
    Read: obj -> obj
}

[<NoComparison; NoEquality>]
type RecordPlan = {
    Fields: FieldPlan[]
    Make: obj[] -> obj
    Title: string
}

(**
One case of a tagged DU. Built once per case, and used from both directions:
decode looks up by wire `Tag`, encode indexes by the case's runtime tag.

invariant: encode and decode read the same case table, so a shape one accepts the other cannot drop
*)
[<NoComparison; NoEquality>]
type CasePlan = {
    /// wire discriminator value — tag rule already applied
    Tag: string
    Info: UnionCaseInfo
    /// `None` for a fieldless case; `Some` for a single record payload, whose
    /// keys flatten alongside the discriminator
    Payload: RecordPlan option
}

[<NoComparison; NoEquality>]
type BuildCtx = {
    Backend: IJsonBackend
    Registry: CodecRegistry
    KeyTransform: string -> string
    TagTransform: string -> string
    /// root-to-node path of compound `FullName`s — see the cycle guard note
    Building: string list
    /// `Some prefix` emits every record/union as `{"$ref": prefix + Name}` and
    /// hoists its body into `Plan.Definitions`; `None` inlines them.
    ///
    /// Flat stays the default: LLM tool-call validators handle flat schemas far
    /// better, which is why the truncating cycle guard was chosen originally.
    /// `$ref` exists for OpenAPI `components/schemas`, where reuse is the point
    /// and a recursive type must round-trip rather than truncate.
    ///
    /// decision: defaults to flat schemas — LLM tool validators handle inline shapes more reliably than `$ref`
    RefMode: string option
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

let private primitiveNode (typeName: string) : JsonSchemaValue =
    SVDict(Map.ofList [ "type", SVStr typeName ])

/// A primitive plus the `format` keyword naming its string encoding —
/// `date-time`, `uuid`, `decimal`.
let private formatNode (typeName: string) (format: string) : JsonSchemaValue =
    SVDict(Map.ofList [ "type", SVStr typeName; "format", SVStr format ])

/// A leaf carries no hoisted definitions — only compound nodes do.
let private noDefs: Map<string, JsonSchemaValue> = Map.empty

/// Union of definition maps gathered from sibling subtrees. Keys are `FullName`s,
/// so a repeated key is genuinely the same type reached twice and the bodies are
/// equal — merging is a re-registration, never a conflict.
let private mergeDefs (maps: Map<string, JsonSchemaValue> list) : Map<string, JsonSchemaValue> =
    maps
    |> List.fold (fun acc m -> Map.fold (fun a k v -> Map.add k v a) acc m) Map.empty

(**
In `$ref` mode, replace a compound node's body with a reference and hand the body
back for hoisting. In flat mode it stays inline and nothing is hoisted.

Keyed by **`FullName`, not `Name`**. Two records both called `Item` in different
modules share a simple name, so keying on it collapsed them into one definition
and every `$ref` to either resolved to whichever merged last — a silently wrong
document rather than a failure. `JsonSchemaGen.shortenDefinitionNames` shortens
the unambiguous ones again on the way out, so published pointers stay readable.

invariant: a definition key identifies exactly one type
*)
let private refOrInline
    (ctx: BuildCtx)
    (key: string)
    (body: JsonSchemaValue)
    (childDefs: Map<string, JsonSchemaValue>)
    : JsonSchemaValue * Map<string, JsonSchemaValue> =
    match ctx.RefMode with
    | Some prefix -> SVDict(Map.ofList [ "$ref", SVStr(prefix + key) ]), Map.add key body childDefs
    | None -> body, childDefs

/// Discriminator key for tagged DUs. Matches the Pydantic / OpenAPI
/// convention. v1 hardcodes it; a future combinator can override per codec.
///
/// Lives here rather than in `Schema` because all three faces that read it —
/// decode, encode and schema emission — are now built in this module.
///
/// decision: fixes the discriminator key to `type` — matches Pydantic/OpenAPI and keeps all plan faces aligned
let discriminatorKey = "type"

// ============================================================================
// The walker
// ============================================================================

(**
Module-scope mutual recursion with explicit parameters.

decision: keeps recursive planning outside inline functions — captured values in nested inline recursion lower to `undefined` on BEAM
*)
let rec forTypeIn (ctx: BuildCtx) (t: System.Type) : Plan =
    let fullName = t.FullName
    let b = ctx.Backend

    // Primitives precede registry lookup, so a codec registered against
    // `System.Int32` stays inert in both directions.
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
        // A user codec drives BOTH directions. `toJsonValue` / `fromJsonValue`
        // are the only places a `JsonValue` is built or unwrapped — they exist
        // solely to cross this boundary.
        | Some entry ->
            let decode = entry.decode
            let encode = entry.encode

            {
                Decode =
                    fun v ->
                        match decode (toJsonValue b v) with
                        | Ok x -> Ok x
                        | Error msg -> leafError msg
                Encode = fun v -> fromJsonValue b (encode v)
                // The codec's own schema, captured at registration — this is
                // how a refined type's constraints reach the emitted document.
                Schema = SVDict entry.schema
                Definitions = noDefs
            }
        | None ->

            // Dispatched AFTER the registry, unlike the five primitives above:
            // a date format or decimal representation is exactly the kind of
            // thing a consumer may need to override, so a registered codec for
            // these still wins.
            if fullName = "System.DateTime" then
                planDateTime b
            elif fullName = "System.DateTimeOffset" then
                planDateTimeOffset b
            elif fullName = "System.Guid" then
                planGuid b
            elif fullName = "System.Decimal" then
                planDecimal b
            // F# list MUST precede the union test: `FSharpList<'T>` is itself a DU,
            // so `IsUnion` returns true for it on the CLR and would mis-route.
            elif isFSharpListType fullName then
                planSeq ctx (getGenericInnerType t) extractList listBuilder
            elif t.IsArray then
                planSeq ctx (t.GetElementType()) extractArray arrayBuilder
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
                // decision: passes unknown types through encode but rejects decode — preserves manual output only
                let message = sprintf "cannot decode %s" fullName

                {
                    Decode = fun _ -> leafError message
                    Encode = id
                    Schema = SVDict emptySchema
                    Definitions = noDefs
                }

// --- Primitive nodes --------------------------------------------------------
//
// Each closure already knows its target type, so per-value calls perform no
// `FullName` dispatch. Conversions come from `Primitives`, shared with `Codec`.

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
    // Primitives are already the backend's native form.
    Encode = id
    Schema = primitiveNode "string"
    Definitions = noDefs
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
    // Primitives are already the backend's native form.
    Encode = id
    Schema = primitiveNode "integer"
    Definitions = noDefs
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
    // Primitives are already the backend's native form.
    Encode = id
    Schema = primitiveNode "integer"
    Definitions = noDefs
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
    // Primitives are already the backend's native form.
    Encode = id
    Schema = primitiveNode "number"
    Definitions = noDefs
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
    // Primitives are already the backend's native form.
    Encode = id
    Schema = primitiveNode "boolean"
    Definitions = noDefs
}

// --- String-encoded scalars -------------------------------------------------
//
// JSON has no date, uuid or exact-decimal type. All three cross the wire as
// strings and carry a `format` keyword naming which one — the schema therefore
// describes exactly what `Encode` produces, which is the property the whole
// single-walker design exists to guarantee.

and private planDateTime (b: IJsonBackend) : Plan = {
    Decode =
        fun v ->
            if b.IsString v then
                match Primitives.parseDateTime (b.AsString v) with
                | Ok d -> Ok(box d)
                | Error msg -> leafError msg
            else
                leafError (sprintf "cannot coerce %s to System.DateTime" (describeValue b v))
    Encode = fun v -> box (Primitives.dateTimeToString (unbox<System.DateTime> v))
    Schema = formatNode "string" "date-time"
    Definitions = noDefs
}

and private planDateTimeOffset (b: IJsonBackend) : Plan = {
    Decode =
        fun v ->
            if b.IsString v then
                match Primitives.parseDateTimeOffset (b.AsString v) with
                | Ok d -> Ok(box d)
                | Error msg -> leafError msg
            else
                leafError (sprintf "cannot coerce %s to System.DateTimeOffset" (describeValue b v))
    Encode = fun v -> box (Primitives.dateTimeOffsetToString (unbox<System.DateTimeOffset> v))
    Schema = formatNode "string" "date-time"
    Definitions = noDefs
}

and private planGuid (b: IJsonBackend) : Plan = {
    Decode =
        fun v ->
            if b.IsString v then
                match Primitives.parseGuid (b.AsString v) with
                | Ok g -> Ok(box g)
                | Error msg -> leafError msg
            else
                leafError (sprintf "cannot coerce %s to System.Guid" (describeValue b v))
    Encode = fun v -> box (Primitives.guidToString (unbox<System.Guid> v))
    Schema = formatNode "string" "uuid"
    Definitions = noDefs
}

and private planDecimal (b: IJsonBackend) : Plan = {
    // A bare JSON number is accepted on the way in — rejecting `12.34` would be
    // gratuitous, and the string form is what this node itself emits.
    //
    // tradeoff: accepts lossy bare-number decimals to interoperate with ordinary JSON number producers
    Decode =
        fun v ->
            if b.IsString v then
                match Primitives.parseDecimal (b.AsString v) with
                | Ok d -> Ok(box d)
                | Error msg -> leafError msg
            elif b.IsInt v then
                Ok(box (decimal (b.AsInt v)))
            elif b.IsFloat v then
                Ok(box (decimal (b.AsFloat v)))
            else
                leafError (sprintf "cannot coerce %s to System.Decimal" (describeValue b v))
    Encode = fun v -> box (Primitives.decimalToString (unbox<decimal> v))
    Schema = formatNode "string" "decimal"
    Definitions = noDefs
}

// --- Sequences --------------------------------------------------------------

(**
`build` turns the collected elements into the target's runtime shape —
`buildList` for `'T list`, `buildArray` for `'T[]`. Both are needed because
CLR generics and arrays are invariant: an `obj list` cannot be assigned to an
`int list` field, nor an `obj[]` to an `int[]` one.

Iteration goes through `ArrayLength` / `ArrayAt` rather than an F# list or
array, because the native sequence shape differs per backend.

decision: stops at the first invalid sequence element — avoids decoding an unused tail after the result is already an error
tradeoff: reports one invalid element per sequence decode in exchange for bounded failure work
*)
and private planSeq (ctx: BuildCtx) (elementType: System.Type) (extract: obj -> obj list) (builder: System.Type -> obj list -> obj) : Plan =
    let b = ctx.Backend
    let element = forTypeIn ctx elementType
    // Resolve the constructor ONCE — `builder` does the generic instantiation
    // when applied, not when the closure it returns is called.
    let build = builder elementType
    let expected = sprintf "expected JSON array for %s[]" elementType.Name

    {
        Schema = SVDict(Map.ofList [ "type", SVStr "array"; "items", element.Schema ])
        Definitions = element.Definitions
        Encode =
            fun v ->
                extract v
                |> List.map element.Encode
                |> b.BuildArray
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
                    | None -> Ok(build (List.rev acc))
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
                Wrap = if isOpt then optionBuilder innerType else id
                Read = fieldReader fi
            })

    {
        Fields = fields
        Make = recordCtor t
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

/// Stage 2, encode side. Mirror of `decodeRecordWith`: same field array, same
/// keys, so the two cannot disagree about what a record looks like on the wire.
///
/// decision: omits absent optional fields — matches their non-required schema and avoids emitting nullable values
and encodeRecordInto (b: IJsonBackend) (rp: RecordPlan) (acc0: obj) (record: obj) : obj =
    let mutable acc = acc0

    for i = 0 to rp.Fields.Length - 1 do
        let f = rp.Fields.[i]
        let v = f.Read record

        if f.Optional then
            match extractOption v with
            | Some inner -> acc <- b.Put(acc, f.Key, f.Inner.Encode inner)
            | None -> ()
        else
            acc <- b.Put(acc, f.Key, f.Inner.Encode v)

    acc

/// The object schema for a record plan. `properties` reads the same `Key` the
/// decoder looks up and the encoder writes, so a case rule or alias cannot
/// apply to two of the three and not the third.
///
/// An optional field contributes its inner type's schema and is simply absent
/// from `required` — JSON Schema has no separate notion of optionality.
and recordSchema (rp: RecordPlan) : JsonSchema =
    let properties =
        rp.Fields
        |> Array.map (fun f -> f.Key, f.Inner.Schema)
        |> Array.toList

    let required =
        rp.Fields
        |> Array.choose (fun f -> if f.Optional then Option.None else Some(SVStr f.Key))
        |> Array.toList

    let baseSchema =
        Map.ofList [
            "type", SVStr "object"
            "title", SVStr rp.Title
            "properties", SVDict(Map.ofList properties)
        ]

    if List.isEmpty required then
        baseSchema
    else
        Map.add "required" (SVList required) baseSchema

and private planRecord (ctx: BuildCtx) (t: System.Type) : Plan =
    let b = ctx.Backend
    let rp = buildRecordPlan ctx t
    let expected = sprintf "expected JSON object for %s" rp.Title

    let childDefs =
        rp.Fields
        |> Array.map (fun f -> f.Inner.Definitions)
        |> Array.toList
        |> mergeDefs

    let schema, defs = refOrInline ctx t.FullName (SVDict(recordSchema rp)) childDefs

    {
        Decode =
            fun v ->
                if b.IsMap v then
                    decodeRecordWith b rp (mapLookup b v)
                else
                    leafError expected
        Encode = fun v -> encodeRecordInto b rp (b.NewMap()) v
        Schema = schema
        Definitions = defs
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
and buildCasePlans (ctx: BuildCtx) (t: System.Type) : CasePlan[] =
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

        {
            Tag = ctx.TagTransform caseInfo.Name
            Info = caseInfo
            Payload = payload
        })

/// Decode-side view: wire tag -> case.
and casesByTag (cases: CasePlan[]) : Map<string, CasePlan> =
    cases
    |> Array.map (fun c -> c.Tag, c)
    |> Map.ofArray

and decodeUnionWith
    (b: IJsonBackend)
    (cases: Map<string, CasePlan>)
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
        | Some c ->
            match c.Payload with
            | None -> Ok(FSharpValue.MakeUnion(c.Info, [||]))
            | Some rp ->
                // The payload record shares the outer lookup — its keys sit
                // alongside the discriminator rather than nested under it.
                decodeRecordWith b rp lookup
                |> Result.map (fun payload -> FSharpValue.MakeUnion(c.Info, [| payload |]))
    | Some _ ->
        Error [
            {
                path = discriminatorKey
                message = sprintf "discriminator '%s' must be a string" discriminatorKey
            }
        ]

/// Encode-side view: the value's runtime case tag indexes straight into the
/// same array decode was built from.
///
/// `caseValues : obj[]` has a different runtime shape per backend — a
/// process-dict ref on BEAM, a GenericArray on Python, a native array on .NET —
/// so payload access goes through `ArrayLength` / `ArrayAt`.
and encodeUnionWith (b: IJsonBackend) (byTag: CasePlan[]) (t: System.Type) (v: obj) : obj =
    let caseInfo, caseValues = FSharpValue.GetUnionFields(v, t)
    let c = byTag.[caseInfo.Tag]
    let acc = b.Put(b.NewMap(), discriminatorKey, box c.Tag)

    match c.Payload with
    | None -> acc
    | Some rp -> encodeRecordInto b rp acc (b.ArrayAt(box caseValues, 0))

and private planUnion (ctx: BuildCtx) (t: System.Type) : Plan =
    let b = ctx.Backend
    let cases = buildCasePlans ctx t
    let byTag = casesByTag cases

    // `UnionCaseInfo.Tag` is the declaration index, so sorting by it gives an
    // array the encode side can index directly with no lookup.
    let byIndex = cases |> Array.sortBy (fun c -> c.Info.Tag)
    let typeName = t.Name
    let expected = sprintf "expected JSON object for %s" typeName

    // `oneOf` over the cases. The old standalone emitter had no union branch
    // at all and fell through to `{}` for any tagged DU, even though decode
    // and encode both handled them — the clearest symptom of the schema
    // walker being a third, separate implementation.
    let caseSchema (c: CasePlan) =
        let discriminator =
            Map.ofList [ discriminatorKey, SVDict(Map.ofList [ "const", SVStr c.Tag ]) ]

        match c.Payload with
        | None ->
            SVDict(
                Map.ofList [
                    "type", SVStr "object"
                    "title", SVStr c.Info.Name
                    "properties", SVDict discriminator
                    "required", SVList [ SVStr discriminatorKey ]
                ]
            )
        | Some rp ->
            // The payload's keys flatten alongside the discriminator, so the
            // schema merges them rather than nesting.
            let payload = recordSchema rp

            let properties =
                match Map.tryFind "properties" payload with
                | Some(SVDict p) -> Map.fold (fun acc k v -> Map.add k v acc) discriminator p
                | _ -> discriminator

            let required =
                match Map.tryFind "required" payload with
                | Some(SVList r) -> SVStr discriminatorKey :: r
                | _ -> [ SVStr discriminatorKey ]

            SVDict(
                Map.ofList [
                    "type", SVStr "object"
                    "title", SVStr c.Info.Name
                    "properties", SVDict properties
                    "required", SVList required
                ]
            )

    let childDefs =
        [
            for c in byIndex do
                match c.Payload with
                | Some rp ->
                    yield!
                        (rp.Fields
                         |> Array.map (fun f -> f.Inner.Definitions)
                         |> Array.toList)
                | None -> ()
        ]
        |> mergeDefs

    let body =
        SVDict(
            Map.ofList [
                "title", SVStr typeName
                "oneOf", SVList [ for c in byIndex -> caseSchema c ]
            ]
        )

    let schema, defs = refOrInline ctx t.FullName body childDefs

    {
        Decode =
            fun v ->
                if b.IsMap v then
                    decodeUnionWith b byTag typeName (mapLookup b v)
                else
                    leafError expected
        Encode = fun v -> encodeUnionWith b byIndex t v
        Schema = schema
        Definitions = defs
    }

// --- Cycle guard ------------------------------------------------------------

and private planDeferred (ctx: BuildCtx) (t: System.Type) : Plan =
    // Restart with an empty path so the deferred walk expands one full level
    // before deferring again. Termination comes from the document running out
    // of nesting, exactly as it did before plans existed.
    let restart = { ctx with Building = [] }

    {
        Decode = fun v -> (forTypeIn restart t).Decode v
        Encode = fun v -> (forTypeIn restart t).Encode v
        Schema =
            match ctx.RefMode with
            // A cycle is exactly what `$ref` is for: the ancestor currently
            // building this type registers its body on the way out, so the
            // reference resolves and a recursive type round-trips instead of
            // truncating.
            // `FullName`, matching the key the ancestor registers under.
            | Some prefix -> SVDict(Map.ofList [ "$ref", SVStr(prefix + t.FullName) ])
            // Flat mode truncates to a title-only object. LLM tool-call
            // validators handle flat schemas far better, and the schema of a
            // cyclic type is lossy past the first hop either way.
            // tradeoff: truncates recursive flat schemas after one cycle because many tool-schema consumers reject `$ref`
            | None -> SVDict(Map.ofList [ "type", SVStr "object"; "title", SVStr t.Name ])
        // The ancestor on `Building` owns the definition; registering it here
        // too would recurse forever.
        Definitions = noDefs
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
            RefMode = None
        }
        t

/// As `forType`, but emitting every record and union as `{"$ref": prefix + Name}`
/// and hoisting the bodies into `Plan.Definitions`. Decode and encode are
/// identical either way — this only changes how the schema face is rendered.
let forTypeWithRefs
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (tagTransform: string -> string)
    (refPrefix: string)
    (t: System.Type)
    : Plan =
    forTypeIn
        {
            Backend = backend
            Registry = registry
            KeyTransform = keyTransform
            TagTransform = tagTransform
            Building = []
            RefMode = Some refPrefix
        }
        t

(**
Decode from a `LookupSource` rather than a native map, for sources that are
not backend-native JSON — `Map<string, string>` tool-call input in particular.
Records and unions read their fields through a lookup anyway, so this costs
nothing; any other top-level type has no keys to read and is rejected.

The dispatch below mirrors `forTypeIn`'s, and has to keep mirroring it. This
is the second decode face of the *same* codec, so the two disagreeing about
who owns a type is exactly the drift the one-walker rule exists to prevent.
Two orderings carry the weight:

- A list or array is an `FSharpType.IsUnion` on the CLR, so it must be tested
  first — `buildCasePlans` on `FSharpList` throws on `Cons`'s two fields.
- A registered codec owns its type ahead of the structural walk — otherwise
  the codec a caller registered decodes their JSON and is silently skipped
  for their string map.

Rejection is per call rather than a throw at construction because every codec
builds this face eagerly, including the `auto<'T list>` ones that only ever
decode JSON.

invariant: `forTypeIn` and `forTypeFromLookup` route a type to the same owner — sequences before unions, registry before structure
*)
let forTypeFromLookup
    (backend: IJsonBackend)
    (registry: CodecRegistry)
    (keyTransform: string -> string)
    (tagTransform: string -> string)
    (t: System.Type)
    : LookupSource -> Result<obj, FieldError list> =
    let ctx = {
        Backend = backend
        Registry = registry
        KeyTransform = keyTransform
        TagTransform = tagTransform
        Building = []
        // Decode only — no schema is rendered from this path.
        RefMode = None
    }

    let fullName = t.FullName

    let keyless () =
        let err = [
            {
                path = ""
                message = sprintf "auto<%s> requires a record or discriminated-union type" t.Name
            }
        ]

        fun (_: LookupSource) -> Error err

    if isFSharpListType fullName || t.IsArray then
        keyless ()
    elif FSharpType.IsRecord t || FSharpType.IsUnion t then
        match tryGetCodecEntry fullName registry with
        // The codec owns the type on both faces, and takes the source whole as
        // one `JMap` — the same value the JSON face passes it.
        | Some entry ->
            let decode = entry.decode

            fun src ->
                match decode (toJsonValue backend (src.AsMap())) with
                | Ok x -> Ok x
                | Error msg -> leafError msg
        | None ->
            if FSharpType.IsRecord t then
                let rp = buildRecordPlan ctx t
                fun src -> decodeRecordWith backend rp src.Get
            else
                let cases = casesByTag (buildCasePlans ctx t)
                let typeName = t.Name
                fun src -> decodeUnionWith backend cases typeName src.Get
    else
        keyless ()
