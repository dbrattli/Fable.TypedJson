#if FABLE_COMPILER
module Program

()
#else
module Program =
    open System
    open System.Reflection
    open Fable.TypedJson.Testing

    /// Walks the test assembly and invokes every static method tagged with
    /// `[<Fact>]` from `Fable.TypedJson.Testing`. Reports per-test results
    /// and a summary line; exits non-zero if anything failed so CI can gate
    /// on it. F# `let`-bound test functions compile to static methods on
    /// the module's CLR type, so reflecting over public+nonpublic statics
    /// catches them all.
    let private runTests () : int =
        let asm = Assembly.GetExecutingAssembly()
        let factType = typeof<FactAttribute>
        let mutable passed = 0
        let mutable failed = 0
        let failures = ResizeArray<string * string>()

        for typ in asm.GetTypes() do
            let bindings =
                BindingFlags.Static
                ||| BindingFlags.Public
                ||| BindingFlags.NonPublic

            for mi in typ.GetMethods(bindings) do
                if mi.GetCustomAttributes(factType, false).Length > 0 then
                    let qualified = sprintf "%s.%s" typ.FullName mi.Name

                    try
                        mi.Invoke(null, [||]) |> ignore
                        passed <- passed + 1
                        printfn "PASS %s" qualified
                    with
                    | :? TargetInvocationException as ex ->
                        failed <- failed + 1
                        let inner = ex.InnerException
                        let msg = if isNull inner then ex.Message else inner.Message
                        printfn "FAIL %s: %s" qualified msg
                        failures.Add(qualified, msg)
                    | ex ->
                        failed <- failed + 1
                        printfn "FAIL %s: %s" qualified ex.Message
                        failures.Add(qualified, ex.Message)

        printfn ""
        printfn "Passed: %d, Failed: %d" passed failed

        if failed > 0 then
            printfn ""
            printfn "Failures:"

            for (name, msg) in failures do
                printfn "  %s — %s" name msg

            1
        else
            0

    [<EntryPoint>]
    let main _ = runTests ()
#endif
