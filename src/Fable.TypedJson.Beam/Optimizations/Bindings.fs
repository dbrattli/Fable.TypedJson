module internal Fable.TypedJson.Beam.Optimizations.Bindings

open Fable.Core
open Fable.TypedJson.Backend

[<Import("try_map_array", "typedjson_beam_sequences")>]
let private tryMapArray (array: obj) (mapping: obj -> Result<'Value, 'Error>) : Result<'Value list, int * 'Error> = nativeOnly

type private NativeArrayMapper() =
    interface IJsonArrayMapper with
        member _.TryMapArray(array, mapping) = tryMapArray array mapping

let createMapper () : IJsonArrayMapper option =
    Some(NativeArrayMapper() :> IJsonArrayMapper)
