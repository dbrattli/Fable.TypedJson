-module(typedjson_beam_sequences).
-export([try_map_array/2]).

%% Map a validated JSON array, returning the first failure with its index.
%%
%% decision: walks native list tails once so decoding is linear and uses no process-dictionary accumulators
%% invariant: visits elements in order, stops at the first error, and lets callback exceptions propagate
%% assumption: inputs are Erlang lists or Fable array references, normalized by fable_utils:to_list/1
%% assumption: Fable represents Result values as {ok, Value} and {error, Error}
-spec try_map_array(term(), fun((term()) -> {ok, term()} | {error, term()})) ->
    {ok, [term()]} | {error, {non_neg_integer(), term()}}.
try_map_array(Array, Mapping) ->
    map_items(fable_utils:to_list(Array), Mapping, 0, []).

map_items([], _Mapping, _Index, Acc) ->
    {ok, lists:reverse(Acc)};
map_items([Item | Tail], Mapping, Index, Acc) ->
    case Mapping(Item) of
        {ok, Value} -> map_items(Tail, Mapping, Index + 1, [Value | Acc]);
        {error, Error} -> {error, {Index, Error}}
    end.
