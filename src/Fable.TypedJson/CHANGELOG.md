---
last_commit_released: 7c75608ced76f1f0a4714a879e70d3dbd8fbd5ce
force_version: 5.4.0
name: Fable.TypedJson
include:
  - ../../**
---

# Changelog

All notable changes to this project will be documented in this file.

All packages in this repository share this version and follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 5.3.0 - 2026-08-23

### 🐞 Bug Fixes

* Release TypedJson packages as a unit (#63) ([7c75608](https://github.com/dbrattli/Fable.TypedJson/commit/7c75608ced76f1f0a4714a879e70d3dbd8fbd5ce))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/c7f374b29578ce922d275fccc6c1d95ff3285635..7c75608ced76f1f0a4714a879e70d3dbd8fbd5ce)</small></strong>

## 5.1.0 - 2026-08-04

### 🚀 Features

* Case rules on the Map<string,string> validation path (#53) ([bc9c2b9](https://github.com/dbrattli/Fable.TypedJson/commit/bc9c2b95e7ee7a98c51ac19abe92c26cdb16a2aa))

### 🐞 Bug Fixes

* String-map dispatch must mirror the JSON walker's (#55) ([02b1f48](https://github.com/dbrattli/Fable.TypedJson/commit/02b1f4805966b8314b4ef0f23a9a4ef38ef3ddaf))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/28ba31ee40ef19a7007ad160fffee5d163d2a497..02b1f4805966b8314b4ef0f23a9a4ef38ef3ddaf)</small></strong>

## 5.0.1 - 2026-08-04

### 🐞 Bug Fixes

* $ref definition-name collision, and backend-independent date parsing (#51) ([28ba31e](https://github.com/dbrattli/Fable.TypedJson/commit/28ba31ee40ef19a7007ad160fffee5d163d2a497))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/0375e30f696c8f2b116a164a8991333e84457993..28ba31ee40ef19a7007ad160fffee5d163d2a497)</small></strong>

## 5.0.0 - 2026-08-03

### 🚀 Features

* $ref/$defs schema mode, schema IR access, and DateTime/Guid/decimal support (#49) ([0375e30](https://github.com/dbrattli/Fable.TypedJson/commit/0375e30f696c8f2b116a164a8991333e84457993))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/d34e1f4c55505495b907aace4de33c34b73822a3..0375e30f696c8f2b116a164a8991333e84457993)</small></strong>

## 5.0.0-rc.1 - 2026-08-02

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/9a1c1d7df3abe8101394a3152a8083dab978be07..d34e1f4c55505495b907aace4de33c34b73822a3)</small></strong>

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
