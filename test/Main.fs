(**
# Main — Entry point for the Fable.TypedJson test suite

One runner for every target. Quill's `runTests` executes the suite and
returns the process exit code, so a failing test fails the build on all
four backends:

  * BEAM   — the run is synchronous and Quill calls `halt/1`, so `erl`
             exits non-zero. Fable emits a `main.erl` shim dispatching to
             `[<EntryPoint>]`, which is what the justfile invokes.
  * Python — synchronous; the returned exit code is the process exit code.
  * JS     — Node cannot block, so Quill chains `process.exit` onto the
             resolved promise and the value returned here is ignored.
  * .NET   — plain CLR execution, no Fable transpile.

adr: Scriptorium (Quill + Nib) replaces the four per-target runners — it compiles to every target we ship
adr: one [<EntryPoint>] for all targets; Fable's BEAM `main.erl` shim keeps it findable across compiler versions
invariant: every test module's `tests` value appears in this list — an unlisted module is silently not run
tradeoff: lost the BEAM runner's "discovered zero modules is a failure" guard; gained one runner and real skips
*)

module Fable.TypedJson.Tests.Main

open type Scriptorium.Quill.Runner

[<EntryPoint>]
let main _ =
    runTests [
        Encode.tests
        Auto.tests
        CaseRules.tests
        Errors.tests
        ErasedDU.tests
        Schema.tests
        Codec.tests
        Refined.tests
        Nested.tests
        JsonSchema.tests
        Alias.tests
        Union.tests
        Scalars.tests
    ]
