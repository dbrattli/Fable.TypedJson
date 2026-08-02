---
force_version: 5.0.0-rc.1
last_commit_released: 9a1c1d7df3abe8101394a3152a8083dab978be07
name: Fable.TypedJson
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 0.4.1 - 2026-08-01

### 🐞 Bug Fixes

* *(schema)* Terminate recursive schema generation, stabilize JSON keys across targets (#41) ([9a1c1d7](https://github.com/dbrattli/Fable.TypedJson/commit/9a1c1d7df3abe8101394a3152a8083dab978be07))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/98a8b39e6ff95f652d9add3b92ce73ae7b781b80..9a1c1d7df3abe8101394a3152a8083dab978be07)</small></strong>

## 0.4.0 - 2026-05-21

### 🚀 Features

* Union tag follows codec CaseRules (snake_case discriminator support) (#16) ([98a8b39](https://github.com/dbrattli/Fable.TypedJson/commit/98a8b39e6ff95f652d9add3b92ce73ae7b781b80))

### 🐞 Bug Fixes

* Pin InvariantCulture for string→float coerce on .NET (#14) ([c41c699](https://github.com/dbrattli/Fable.TypedJson/commit/c41c699ec56cbfd68f638cb3ba8c03fb0e3ebd1a))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/fff65d7af324f670816212bbd6d4efa7db712b1e..98a8b39e6ff95f652d9add3b92ce73ae7b781b80)</small></strong>

## 0.3.0 - 2026-05-03

### 🚀 Features

* Add .NET backend shim (Fable.TypedJson.DotNet) (#7) ([b6ed245](https://github.com/dbrattli/Fable.TypedJson/commit/b6ed2459ff6e83e32dba3b668164594f0edc201f))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/6d52d3074b3a8fde66276ac5540ecd0cd1f08cd6..fff65d7af324f670816212bbd6d4efa7db712b1e)</small></strong>

## 0.2.0 - 2026-05-03

### 🚀 Features

* Add JavaScript backend shim (Fable.TypedJson.JS) (#4) ([df883bb](https://github.com/dbrattli/Fable.TypedJson/commit/df883bb1caf6781d6cbd2733e2d63453f7afd207))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/fcd1e23d9e94e4527dd68dbba0e0bc82d7e4f158..6d52d3074b3a8fde66276ac5540ecd0cd1f08cd6)</small></strong>

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
