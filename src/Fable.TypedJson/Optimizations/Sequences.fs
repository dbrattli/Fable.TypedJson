module internal Fable.TypedJson.Optimizations.Sequences

open Fable.TypedJson.Backend

/// Select traversal once while preserving the caller's constructors and errors.
///
/// decision: inlines selection into codec construction so indexed backends retain their existing decode loop
/// invariant: elements are visited in order and traversal stops at the first error
let inline createDecoder
    (backend: IJsonBackend)
    (decodeElement: obj -> Result<'Value, 'Error>)
    (build: 'Value list -> 'Sequence)
    (invalid: unit -> Result<'Sequence, 'Error>)
    (atIndex: int -> 'Error -> 'Error)
    : obj -> Result<'Sequence, 'Error> =
    match backend.ArrayMapper with
    | Some mapper ->
        fun value ->
            if not (backend.IsArray value) then
                invalid ()
            else
                match mapper.TryMapArray(value, decodeElement) with
                | Ok items -> Ok(build items)
                | Error(index, errors) -> Error(atIndex index errors)
    | None ->
        fun value ->
            if not (backend.IsArray value) then
                invalid ()
            else
                let len = backend.ArrayLength value
                let mutable i = 0
                let mutable failure: 'Error option = None
                let mutable acc: 'Value list = []

                while i < len && failure.IsNone do
                    match decodeElement (backend.ArrayAt(value, i)) with
                    | Ok item -> acc <- item :: acc
                    | Error errors -> failure <- Some(atIndex i errors)

                    i <- i + 1

                match failure with
                | Some errors -> Error errors
                | None -> Ok(build (List.rev acc))
