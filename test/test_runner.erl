-module(test_runner).
-export([main/1]).

%% Test runner for Fable.TypedJson tests.
%% Discovers every module in Dir that exports at least one test_*/0 function
%% and runs those functions.
%%
%% Discovery is by exports, not by module name: Fable's BEAM module naming is
%% not stable across compiler versions (5.5 emitted `test_alias`, 5.11 emits
%% `fable_typed_json_test_test_alias`), and a name-prefix filter silently
%% matched zero modules on upgrade while still reporting success.

main([Dir]) ->
    io:format("~n=== Fable.TypedJson Test Suite ===~n~n"),
    Beams = filelib:wildcard(filename:join(Dir, "*.beam")),
    Modules = [M || F <- Beams,
               M <- [list_to_atom(filename:basename(F, ".beam"))],
               has_tests(M)],
    {TotalPass, TotalFail} = lists:foldl(
        fun(Mod, {AccPass, AccFail}) ->
            code:purge(Mod),
            code:load_file(Mod),
            {P, F} = run_module(Mod),
            {AccPass + P, AccFail + F}
        end,
        {0, 0},
        lists:sort(Modules)
    ),
    Total = TotalPass + TotalFail,
    io:format("~n=== Results ===~n"),
    io:format("Total: ~p | Passed: ~p | Failed: ~p~n",
              [Total, TotalPass, TotalFail]),
    case {Total, TotalFail} of
        %% A run that discovers nothing is a broken runner, not a green suite.
        {0, _} -> io:format("~nNo tests discovered in ~s!~n", [Dir]), halt(1);
        {_, 0} -> io:format("~nAll tests passed!~n"), ok;
        _ -> io:format("~nSome tests FAILED!~n"), halt(1)
    end.

has_tests(Mod) ->
    code:ensure_loaded(Mod),
    erlang:function_exported(Mod, module_info, 0) andalso test_funs(Mod) =/= [].

test_funs(Mod) ->
    [F || {F, 0} <- Mod:module_info(exports),
     F =/= module_info,
     lists:prefix("test_", atom_to_list(F))].

run_module(Mod) ->
    io:format("--- ~s ---~n", [Mod]),
    TestFuns = test_funs(Mod),
    lists:foldl(
        fun(Fun, {Pass, Fail}) ->
            case run_test(Mod, Fun) of
                pass -> {Pass + 1, Fail};
                fail -> {Pass, Fail + 1}
            end
        end,
        {0, 0},
        lists:sort(TestFuns)
    ).

run_test(Mod, Fun) ->
    try
        Mod:Fun(),
        io:format("  \e[32m✓\e[0m ~s~n", [Fun]),
        pass
    catch
        error:{assertEqual, Props} ->
            Expected = proplists:get_value(expected, Props),
            Actual = proplists:get_value(value, Props),
            io:format("  \e[31m✗\e[0m ~s~n    expected: ~p~n    actual:   ~p~n", [Fun, Expected, Actual]),
            fail;
        Class:Reason:Stack ->
            io:format("  \e[31m✗\e[0m ~s~n    ~p:~p~n    ~p~n", [Fun, Class, Reason, Stack]),
            fail
    end.
