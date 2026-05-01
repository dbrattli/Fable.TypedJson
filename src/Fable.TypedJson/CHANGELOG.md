---
name: Fable.TypedJson
# last_commit_released will be set by ShipIt on the first published release.
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 0.1.0

### 🚀 Features

* Initial release: Pydantic-flavored JSON validation core.
* `IJsonCodec<'T>` interface and validators-as-types pattern.
* Reflection-driven `auto<'T>` with nested records and `'T list` / `'T[]` support.
* `Codec` combinator pipeline: `gt`, `lt`, `ge`, `le`, `minLength`, `maxLength`, `nonEmpty`, `pattern`, `refine`, `map`, `describe`.
* Bundled refined types: `NonEmptyString`, `PositiveInt`, `NonNegativeInt`, `Email`, `Url`, `Uuid` (`Refined.registerAll`).
* Field aliases (`TypedJson.alias`) and cross-field validators (`TypedJson.withModel`).
* JSON Schema generation (`jsonSchemaOf<'T>` / `jsonSchemaOfCodec`).
* `IJsonBackend` abstraction for cross-target shims.
