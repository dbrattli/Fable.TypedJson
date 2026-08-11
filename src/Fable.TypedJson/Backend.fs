(**
# IJsonBackend — Cross-target JSON map abstraction

Each Fable target (BEAM, JS, Python, .NET) provides a concrete IJsonBackend
implementation. The core Schema and TypedJson layers operate through this
interface so the same codec works across all backends.

decision: keeps the core format-agnostic behind backend shims — one plan runs on all supported targets
decision: carries native JSON values as `obj` — each target has an incompatible runtime representation
*)

module Fable.TypedJson.Backend

/// A JSON map abstraction over the backend's native representation:
///   - JS: a plain object `{ ... }`
///   - Python: a `dict`
///   - BEAM: a `BeamMap<string, obj>`
///   - .NET: a `Dictionary<string, JsonValue>` wrapped in `JMap`
///
/// Each backend also supplies type-test methods so plan nodes can dispatch on
/// a parsed value's runtime type without first constructing a `JsonValue`.
/// This is required because the runtime
/// representation of "an integer" differs across targets — Python in
/// particular wraps F# `int` in an `int32` class, so an `isinstance(x, int32)`
/// check (the F# pattern-match shape) misses native ints from `json.loads`.
type IJsonBackend =
    // --- Map operations ---
    /// Create a fresh, empty map.
    abstract member NewMap: unit -> obj
    /// True if `key` exists in `map`.
    abstract member ContainsKey: map: obj * key: string -> bool
    /// Get the raw value at `key`. Behavior is undefined if the key is missing —
    /// callers should use `ContainsKey` first.
    abstract member Get: map: obj * key: string -> obj
    /// Set `key` to `value` and return the map to use for the NEXT call.
    ///
    /// Linear ownership: the map passed in is DEAD once this returns, and
    /// callers must use only the returned value. That licenses backends to
    /// mutate in place. Every call site is an accumulator fold that never
    /// reuses its argument, so linear ownership holds.
    ///
    /// decision: gives `Put` linear ownership — backends may mutate and avoid per-key map copies
    /// invariant: a map handed to `Put` is never read again by the caller
    abstract member Put: map: obj * key: string * value: obj -> obj
    /// Parse a JSON string into the backend's native map representation.
    /// Named `ParseRaw` to emphasize the result is the raw native value
    /// (Erlang map, Python dict, ...), not a validated record.
    abstract member ParseRaw: json: string -> obj
    /// Serialize a value (typically a map produced by NewMap/Put) to a JSON string.
    abstract member Stringify: value: obj -> string
    /// The backend's representation of JSON null. F# `null` is not portable —
    /// on BEAM it compiles to the `undefined` atom, which jsx serializes as
    /// the string `"undefined"` instead of JSON null. Backends supply the
    /// right sentinel here.
    abstract member Null: obj

    // --- Type tests (used by primitive plan nodes and codecs) ---
    /// True if `value` is the backend's representation of a JSON string.
    abstract member IsString: value: obj -> bool
    /// True if `value` is an integer (and not a bool, where backends conflate them).
    abstract member IsInt: value: obj -> bool
    /// True if `value` is a floating-point number.
    abstract member IsFloat: value: obj -> bool
    /// True if `value` is a boolean.
    abstract member IsBool: value: obj -> bool
    /// True if `value` is the backend's representation of JSON null.
    abstract member IsNull: value: obj -> bool
    /// True if `value` is a JSON array (list).
    abstract member IsArray: value: obj -> bool
    /// True if `value` is a JSON object (map / dict).
    abstract member IsMap: value: obj -> bool

    // --- Typed accessors ---
    // Pair with the IsX type tests: plan nodes classify through the backend,
    // then pull the typed value via these accessors. The hot path operates on
    // each backend's native shape rather than constructing `JsonValue` cases.
    /// Read `value` as a string. Behaviour is undefined unless `IsString value`.
    abstract member AsString: value: obj -> string
    /// Read `value` as an int. Behaviour is undefined unless `IsInt value`.
    abstract member AsInt: value: obj -> int
    /// Read `value` as a float. Behaviour is undefined unless `IsFloat value`.
    abstract member AsFloat: value: obj -> float
    /// Read `value` as a bool. Behaviour is undefined unless `IsBool value`.
    abstract member AsBool: value: obj -> bool
    /// Length of a JSON array. Used for iterating without committing to a
    /// particular F# list/array runtime shape — Python's `json.loads` returns
    /// a native list while BEAM's `jsx.decode` returns an Erlang list, and
    /// neither aligns with Fable's `FSharpList` cons-cell representation.
    abstract member ArrayLength: array: obj -> int
    /// Element at `index` in a JSON array (0-based).
    abstract member ArrayAt: array: obj * index: int -> obj
    /// Build a JSON array from F# list of values. Each backend produces its
    /// native sequence form so the subsequent `Stringify` emits a JSON array
    /// rather than an internal F# list representation.
    abstract member BuildArray: items: obj list -> obj
