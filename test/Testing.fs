(**
# Testing — Test utilities for Fable.TypedJson

Cross-backend test helpers. `equal` raises when expected ≠ actual so that
both BEAM (whose `Fable.Core.Testing.Assert.AreEqual` silently returns a
bool) and Python's pytest see real failures.
*)

module Fable.TypedJson.Testing

type FactAttribute() =
    inherit System.Attribute()

let inline equal expected actual : unit =
    if not (LanguagePrimitives.GenericEquality expected actual) then
        failwithf "expected %A but got %A" expected actual

let inline notEqual expected actual : unit =
    if LanguagePrimitives.GenericEquality expected actual then
        failwithf "expected NOT %A but got %A" expected actual
