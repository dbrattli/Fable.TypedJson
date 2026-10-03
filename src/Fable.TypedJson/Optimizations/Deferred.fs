module internal Fable.TypedJson.Optimizations.Deferred

/// Choose deferred-plan reuse without exposing target checks to the planner.
///
/// decision: memoizes per codec on .NET, JS, and Python so reached depths are reused
/// tradeoff: retains plans up to the deepest visited level to avoid repeated recursive reflection
/// tradeoff: rebuilds on BEAM because Lazy stores process-local references that cannot safely travel with a codec
let inline createResolver (build: unit -> 'Plan) : unit -> 'Plan =
#if FABLE_COMPILER_BEAM
    build
#else
    let resolved = lazy (build ())
    fun () -> resolved.Value
#endif
