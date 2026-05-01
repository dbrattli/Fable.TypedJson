# GRIT: Literate Spec-Driven Development

> Reference this file in your CLAUDE.md / AGENTS.md:
> `See GRIT.md for the annotation convention used in this codebase.`

---

## You are expected to write GRIT directives

When you write, modify, or refactor code you **must** annotate your decisions
using GRIT directives. This is not optional documentation — it is the
**review contract** between you and the human reviewer.

The reviewer will read your directives *before* reading your code. If the
directives are wrong, incomplete, or missing, the review fails regardless of
whether the code works.

---

## The directive vocabulary

```text
adr:        A decision you made. States what and why. Alternatives are implicitly rejected.
invariant:  A truth that must always hold. Future code must never violate this.
principle:  A design philosophy guiding this and future decisions.
assumption: Something you believe to be true that could be wrong.
tradeoff:   What you sacrificed and what you gained.
```

Start with `adr:` and `invariant:`. They cover 80% of cases.

---

## Format rules

- **One directive per line**
- **Under 120 characters** — if you need more, write prose, not a directive
- **Present tense** — "uses fold" not "was changed to use fold"
- **Co-located** — place directives in the block comment immediately wrapping the code they describe
- **Be specific** — "fold over recursion — stack safety on large inputs" not "chosen for performance"

---

## Examples

### ✅ Good — specific, falsifiable, co-located

```fsharp
(**
adr: fold over recursive descent — stack safety for inputs exceeding 10k lines
invariant: blocks accumulate in reverse order; List.rev applied exactly once at end
assumption: caller has split content on \n before passing to parse
*)
let parse (lines: string seq) : Document =
    lines |> Seq.fold parseLine initial |> flushState |> _.Blocks |> List.rev
```

```python
def process_events(queue):
    """
    adr: single consumer loop over thread pool — event ordering must be preserved
    invariant: queue is drained completely before function returns
    tradeoff: throughput limited to single thread — acceptable, ordering is non-negotiable
    """
```

```typescript
/**
 * adr: immutable updates via spread — enables time-travel debugging
 * invariant: state.version increments monotonically, never resets or decrements
 * tradeoff: O(n) copy per update — acceptable for UI state, not for large collections
 */
function reducer(state: State, action: Action): State { ... }
```

### ❌ Bad — vague, not falsifiable, missing

```fsharp
(** Use fold for better performance *)
let parse (lines: string seq) : Document = ...
```

```python
def process_events(queue):
    # processes events
```

---

## What the reviewer will do with your directives

The reviewer reads your directives first. They will:

1. **Agree or challenge the `adr:`** — was this the right decision?
2. **Verify the `invariant:`** — does the code actually uphold it?
3. **Probe the `assumption:`** — is this actually true in the calling context?
4. **Squint at the implementation** — if directives are sound and tests are green, a brief scan is enough

If your `assumption:` is wrong, the reviewer will catch it without reading the
implementation. That is the point. **Surface your reasoning — don't hide it in
the code.**

---

## GRIT is hierarchical — scan downward before touching anything

GRIT directives are scoped. Before modifying any code, scan from the **top of
the file downward** to the specific site you are editing, collecting all
directives in scope:

```
File level          →  applies to everything in the file
  Module level      →  narrows file-level directives
    Type level      →  narrows module-level directives
      Function      →  most specific; can narrow or override above
```

A directive at a narrower scope takes precedence over a broader one.
A function-level `adr:` can legitimately depart from a file-level `principle:` —
but it must do so **explicitly**, acknowledging the departure:

```fsharp
(** principle: immutability by default — all state changes via return values *)
module Parser =

    (**
    adr: mutable accumulator — exception to file-level immutability principle
    tradeoff: breaks immutability convention; justified by 10x performance on hot path
    invariant: mutation is contained within this function; never leaks to caller
    *)
    let parseHot (lines: string seq) : Block list = ...
```

### The scanning rule

When you open a file to make a change:

1. **Read file-level directives first** — these are the standing rules
2. **Read module/type-level directives** — these narrow the rules for this scope
3. **Read function-level directives** — these are the most specific constraints
4. **Collect the full picture** before writing a single line of code

If you skip this scan and violate a directive you didn't notice, the review
contract is broken. *"I didn't see it"* is not acceptable — the directives
are in the file you opened.

---

## You must respect existing GRIT directives

Before writing or modifying any code, **read the GRIT directives already present**
in the file, module, function, or method you are touching.

Existing directives are **binding constraints**, not suggestions:

- An `invariant:` must be preserved — your code must uphold it after your change
- An `adr:` records a decision already made — do not silently reverse it
- An `assumption:` defines what the caller must guarantee — do not violate it from the call site
- A `principle:` governs the design direction — stay consistent with it

### If you disagree with an existing directive

Do not silently violate it. Instead, **challenge it explicitly**:

1. State which directive you are challenging and why
2. Propose the updated directive
3. Wait for human confirmation before proceeding

```
I found this directive in Parser.fs:
  invariant: stack depth is O(1) regardless of input size

My proposed change introduces recursion which would violate this.
Either I restructure to preserve the invariant, or we update it to:
  invariant: stack depth is O(1) for inputs under 10k lines; recursive for smaller inputs

Which do you prefer?
```

Silently breaking a documented invariant is the worst possible outcome —
worse than no GRIT at all, because it creates false confidence in the reviewer.

---

## When to write directives

Write GRIT directives when you:

- Choose one algorithm or data structure over another
- Impose an ordering or sequencing constraint
- Make a decision that future code must not accidentally undo
- Rely on something the caller must guarantee
- Accept a known limitation in exchange for something else
- Implement something non-obvious that a competent reviewer would question

You do **not** need directives for:

- Self-evident code (`invariant: x is always an integer` — no)
- Restating what the type system already enforces
- Every single function — use judgment, annotate decisions not mechanics

---

## GRIT in literate files

In fully literate files (Fable.Literate, Jupytext), use GRIT directives to
**crystallize** the key contracts inside longer prose blocks. The prose
explains the journey; the directive states the destination:

```fsharp
(**
The parser accumulates blocks using a fold rather than building a recursive
call stack. This was necessary after profiling showed that real-world literate
files regularly exceed 5000 lines, causing stack overflows in the recursive
prototype.

invariant: stack depth is O(1) regardless of input size
adr: fold over recursion — verified against 10k line files in production
*)
```

The prose gives context. The directive gives the contract. A reviewer skimming
fast sees the directive. A newcomer reads the full block. An AI agent in the
next session reads both and knows what it must preserve.

---

## The three-layer contract

```
Types     →  machine-verified contracts about data shape
Tests     →  machine-verified contracts about behaviour
GRIT      →  human+AI-verified contracts about intent
```

All three must hold. Green tests with missing GRIT directives is an
incomplete review contract.

---

## GRIT in the ecosystem

```
Conventional Commits   →  semantic intent in commit history
Conventional Comments  →  semantic intent in review feedback
GRIT                   →  semantic intent in source code
```

GRIT completes the trilogy. The same discipline — minimal vocabulary,
machine-readable, human-memorable — applied to the source itself.

---

*GRIT: Literate Spec-Driven Development — <https://github.com/YOUR_ORG/grit>*
