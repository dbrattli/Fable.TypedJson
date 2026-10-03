# Choosing a JSON library

[Overview](overview.md) · [Performance](performance.md)

Fable.TypedJson fits applications where F# models define the data shape, custom
field types carry validation, and JSON Schema is needed alongside decoding and
encoding. Its target adapters support BEAM, Python, JavaScript, and .NET.

## Thoth.Json

[Thoth.Json](https://thoth-org.github.io/Thoth.Json/) provides composable
decoders and encoders, automatic generation from F# types, and custom
representation hooks. Its explicit decoder style is useful when you want
to describe JSON field access and transformations directly.

TypedJson makes derived codecs the main workflow. Register a wrapper type's
codec, compose constraints through helpers such as `Codec.gt` and
`Codec.minLength`, and derive the containing record codec. Declarative
constraints feed JSON Schema as well as decoding.

TypedJson defaults to coercing primitive decoding and also supplies opt-in
strict constructors. It collects errors across record fields; collections
stop at the first failing element. Evaluate the input and error behavior your
application needs, along with the specific target packages of either library.

## Fable.SimpleJson

[Fable.SimpleJson](https://github.com/Zaid-Ajaj/Fable.SimpleJson) treats JSON as
a data structure for parsing, inspecting, and transforming. It also offers
automatic conversion to typed values. That approach suits work where the JSON
structure itself needs manipulation before or without selecting an F# model.

TypedJson focuses on model validation and serialization. Its main result is
a typed F# value or validation errors with paths, and its custom codecs describe
their wire representation through schema fragments.

## Performance comparisons

Use comparable fixtures and account for parsing, construction, and allocation
when comparing libraries. The [Performance guide](performance.md) preserves
the old README's .NET measurements with their limitations. Results from one
runtime do not establish relative performance on another.
