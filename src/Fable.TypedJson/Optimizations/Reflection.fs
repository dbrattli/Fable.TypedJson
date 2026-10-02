module Fable.TypedJson.Optimizations.Reflection

open FSharp.Reflection

(**
## Variance helpers — `extractOption` / `extractList`

Fable backends compile F# generics with type erasure, so `unbox<obj option>
(box (Some "x"))` round-trips by runtime identity. The CLR keeps `'T option`
generic-invariant, so the same `unbox` raises `InvalidCastException` on the
.NET shim. Fable uses erased runtime identities; the CLR branch resolves typed
constructors and readers when a codec is built.

assumption: F# runtime tags use 1 for `Some`/`Cons` and 0 for `None`/`Empty`
*)

#if FABLE_COMPILER
let inline extractOption (v: obj) : obj option = unbox<obj option> v

let inline extractList (v: obj) : obj list = unbox<obj list> v

/// Read a `'T[]` as an `obj list`. Erasure makes every array share a runtime
/// representation on Fable, so the unbox is free.
let inline extractArray (v: obj) : obj list = unbox<obj[]> v |> List.ofArray

// Constructor factories. Erasure makes every option cell / list / array share
// one runtime representation on Fable, so there is nothing to resolve and the
// returned closure is the whole implementation.
let inline optionBuilder (_innerType: System.Type) : obj -> obj = fun v -> box (Some v)

let inline listBuilder (_elementType: System.Type) : obj list -> obj = fun xs -> box xs

let inline arrayBuilder (_elementType: System.Type) : obj list -> obj = fun xs -> box (List.toArray xs)

let inline recordCtor (t: System.Type) : obj[] -> obj =
    fun values -> FSharpValue.MakeRecord(t, values)

let inline fieldReader (fi: System.Reflection.PropertyInfo) : obj -> obj =
    fun record -> FSharpValue.GetRecordField(record, fi)
#else
let extractOption (v: obj) : obj option =
    if isNull v then
        None
    else
        let case, fields = FSharpValue.GetUnionFields(v, v.GetType())

        if case.Tag = 1 then Some fields.[0] else None

let extractList (v: obj) : obj list =
    let typ = v.GetType()
    let mutable current = v
    let mutable acc: obj list = []
    let mutable finished = false

    while not finished do
        let case, fields = FSharpValue.GetUnionFields(current, typ)
        // FSharpList: Empty has Tag 0 (Nil), Cons has Tag 1 (head :: tail).
        if case.Tag = 1 then
            acc <- fields.[0] :: acc
            current <- fields.[1]
        else
            finished <- true

    List.rev acc

(**
## Constructor factories

Each takes a `System.Type` and returns a closure. All the reflection —
`MakeGenericType`, `GetUnionCases`, the `Array.find` for a case — happens once,
when the factory is applied; the returned closure only constructs.

That split is the point. These used to be plain two-argument functions, so
partially applying the type looked like pre-baking but resolved the generic
instantiation again on **every** call. A list field re-derived `FSharpList<'T>`
and both its union cases per decode, which is what a nested benchmark
surfaced: 44 us and 30 KB to decode a small nested document, slower than the
reflection-driven library it is meant to beat.

`PreCompute*` builds a delegate once instead of dispatching through
`MakeUnion` / `MakeRecord` per call, and is typically several times faster. It
is CLR-only — Fable does not implement it, which is why the Fable branch above
returns the direct form.

decision: factories return closures so generic resolution is paid per codec, not per value
invariant: nothing below this line runs reflection inside the returned closure
*)
/// Read a `'T[]` as an `obj list`. Goes through the non-generic `System.Array`
/// rather than `unbox<obj[]>`: CLR array covariance covers reference types
/// only, so `unbox<obj[]> (box [| 1; 2 |])` raises for every value-type element.
let extractArray (v: obj) : obj list =
    let arr = v :?> System.Array
    [ for i in 0 .. arr.Length - 1 -> arr.GetValue i ]

let optionBuilder (innerType: System.Type) : obj -> obj =
    let optType = typedefof<_ option>.MakeGenericType([| innerType |])

    let someCase =
        FSharpType.GetUnionCases(optType)
        |> Array.find (fun c -> c.Tag = 1)

    let ctor = FSharpValue.PreComputeUnionConstructor someCase
    fun v -> ctor [| v |]

let listBuilder (elementType: System.Type) : obj list -> obj =
    let listType = typedefof<_ list>.MakeGenericType([| elementType |])
    let cases = FSharpType.GetUnionCases(listType)

    // FSharpList: Empty has Tag 0 (Nil), Cons has Tag 1 (head :: tail).
    let mkEmpty =
        cases
        |> Array.find (fun c -> c.Tag = 0)
        |> FSharpValue.PreComputeUnionConstructor

    let mkCons =
        cases
        |> Array.find (fun c -> c.Tag = 1)
        |> FSharpValue.PreComputeUnionConstructor

    fun xs ->
        let mutable acc = mkEmpty [||]

        for x in List.rev xs do
            acc <- mkCons [| x; acc |]

        acc

/// A typed `'elementType[]`. The counterpart to `listBuilder` that the array
/// path was missing: a plain `List.toArray` yields `obj[]`, which
/// `MakeRecord` rejects for an `int[]` field — and for a `string[]` field too,
/// since assignment checks exact array type.
let arrayBuilder (elementType: System.Type) : obj list -> obj =
    fun xs ->
        let arr = System.Array.CreateInstance(elementType, List.length xs)
        xs |> List.iteri (fun i x -> arr.SetValue(x, i))
        box arr

let recordCtor (t: System.Type) : obj[] -> obj =
    FSharpValue.PreComputeRecordConstructor t

let fieldReader (fi: System.Reflection.PropertyInfo) : obj -> obj =
    FSharpValue.PreComputeRecordFieldReader fi
#endif
