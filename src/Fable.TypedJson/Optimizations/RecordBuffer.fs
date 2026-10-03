module internal Fable.TypedJson.Optimizations.RecordBuffer

open Fable.Core

#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
[<Emit("new Array($0)")>]
let private nativeCreate (length: int) : obj array = nativeOnly
#endif

/// Allocate scratch space for one record decode.
///
/// decision: skips JS initialization because successful record decoding overwrites every slot
/// invariant: callers write every slot before reading the buffer and discard it on decode failure
let inline create (length: int) : obj array =
#if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
    nativeCreate length
#else
    Array.zeroCreate<obj> length
#endif
