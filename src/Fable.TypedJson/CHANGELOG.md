---
last_commit_released: fcd1e23d9e94e4527dd68dbba0e0bc82d7e4f158
name: Fable.TypedJson
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 0.2.0-rc.1 - 2026-05-01

### 🚀 Features

* Initial release ([09a20d5](https://github.com/dbrattli/Fable.TypedJson/commit/09a20d5e29120de4c9051403039656e325351422))
* Bake CaseRules into TypedJson codec (default LowerFirst) ([70b65c3](https://github.com/dbrattli/Fable.TypedJson/commit/70b65c3a9467a83431ce5d42c3329cc609805209))
* Tagged discriminated unions ([fcd1e23](https://github.com/dbrattli/Fable.TypedJson/commit/fcd1e23d9e94e4527dd68dbba0e0bc82d7e4f158))

### 🐞 Bug Fixes

* *(encode)* Apply CaseRules recursively to nested records and lists ([4f258bf](https://github.com/dbrattli/Fable.TypedJson/commit/4f258bf3bf4e2b41a592c101a5dbab1e8f7ef284))

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
