# JSON Schema

[Overview](overview.md) · [Wire format](wire-format.md) · [Architecture →](architecture.md)

Schema generation uses the same type planner as decoding and encoding. It
includes configured field names, required fields, tagged-union shapes, and
declarative constraints supplied by registered codecs.

## Describe a configured codec

```fsharp
open Fable.TypedJson.Schema
open Fable.TypedJson.Refined
open Fable.TypedJson.Json
open Fable.TypedJson.Beam.Json

type Account = { Username: NonEmptyString; Email: Email }

let registry = emptyRegistry |> registerAll
let codec = autoWith<Account> registry |> alias "Username" "user_name"
let schemaJson = jsonSchemaOfCodec registry codec
```

The resulting object schema has required `user_name` and `email` properties.
`user_name` has type `string` and `minLength: 1`; `email` has type `string` and
the bundled email pattern. Aliases and case rules apply at every nested level.

Pass the same registry used to construct the codec. `jsonSchemaOfCodec` reads
the codec's naming configuration but does not recover its captured registry:
it builds a new plan with the registry you supply. Generate and retain schema
outside repeated request handling.

## Choose an entry point

| Function | Result | Naming configuration |
| --- | --- | --- |
| `jsonSchemaOf<'T> registry rules` | JSON string | Explicit case rule, no aliases |
| `jsonSchemaOfCodec registry codec` | JSON string | Codec's case rules and aliases |
| `jsonSchemaValueOf<'T> registry rules` | `JsonSchemaValue` tree | Explicit case rule, no aliases |
| `jsonSchemaWithDefsOf<'T> registry rules prefix` | Root tree and definitions map | Explicit case rule, no aliases |
| `jsonSchemaWithDefsOfCodec registry codec prefix` | Root tree and definitions map | Codec's case rules and aliases |

Adapters pre-apply the backend to these functions. `JsonSchemaValue` is a typed
tree with string, integer, float, Boolean, list, and dictionary cases. Use it to
compose a larger schema without parsing generated JSON back into a tree.

## Constraints and limits

Numeric bounds, string lengths, patterns, and descriptions contribute their
matching schema keywords. `Codec.map` preserves the source schema while
changing the F# type. A custom `IJsonCodec` supplies its own schema fragment.

Arbitrary `Codec.refine` functions and `withModel` validators have no automatic
schema representation. Coercing decoding may accept more source types than
the schema describes. Optional record fields are omitted from `required`;
their schemas describe the inner type, even though decoding also accepts null.
See [Validation](validation.md) and [Wire format](wire-format.md) for these
runtime distinctions.

## Recursive types and definitions

The flat functions inline nested schemas. When a type refers back to itself,
they stop that cycle with a title-only object schema, losing constraints beyond
that boundary. Flat output can suit consumers that do not accept references.

Use definitions mode for a complete recursive description:

```fsharp
type Tree = { Label: string; Children: Tree list }

let treeCodec = auto<Tree> ()
let root, definitions =
    jsonSchemaWithDefsOfCodec emptyRegistry treeCodec "#/$defs/"

let document =
    match root with
    | SVDict properties ->
        SVDict (Map.add "$defs" (SVDict definitions) properties)
    | _ -> failwith "Expected an object schema fragment"

let documentJson =
    Fable.TypedJson.JsonSchemaGen.schemaValueToJson beam document
```

Definitions mode returns a root fragment and a map of definitions; it does not
assemble the complete document for you. Attaching the definitions under `$defs`
makes the generated `#/$defs/…` references resolve. Replace `beam` with the
adapter's `js`, `python`, or `dotnet` value when changing targets.

Unambiguous definition names are shortened to simple type names. If distinct
types share a simple name, their qualified names are retained to avoid collisions.

## OpenAPI integration

Use `#/components/schemas/` as the prefix when definitions will live in an
OpenAPI components map:

```fsharp
let apiRoot, apiDefinitions =
    jsonSchemaWithDefsOfCodec emptyRegistry treeCodec "#/components/schemas/"

let components = SVDict (Map.empty |> Map.add "schemas" (SVDict apiDefinitions))
```

Attach `components` to your OpenAPI document and use `apiRoot` where the model's
schema is needed. The library provides schema fragments, not a complete OpenAPI
document. Choose a reference prefix matching the location where you place the
definitions, and check compatibility with the consumer's schema dialect.
