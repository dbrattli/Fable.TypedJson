---
last_commit_released: fff65d7af324f670816212bbd6d4efa7db712b1e
name: Fable.TypedJson.Beam
force_version: 0.4.0
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 0.3.0 - 2026-05-03

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/fcd1e23d9e94e4527dd68dbba0e0bc82d7e4f158..fff65d7af324f670816212bbd6d4efa7db712b1e)</small></strong>

## 0.2.0-rc.1 - 2026-05-01

### 🚀 Features

* Initial release ([09a20d5](https://github.com/dbrattli/Fable.TypedJson/commit/09a20d5e29120de4c9051403039656e325351422))
* Bake CaseRules into TypedJson codec (default LowerFirst) ([70b65c3](https://github.com/dbrattli/Fable.TypedJson/commit/70b65c3a9467a83431ce5d42c3329cc609805209))

## 0.1.0

### 🚀 Features

* Initial release: BEAM (Erlang) shim for `Fable.TypedJson`.
* `BeamBackend` implementing `IJsonBackend` via `Fable.Beam.Maps` and `Fable.Beam.Jsx.Jsx` (`jsx.decode` / `jsx.encode`).
* `Fable.TypedJson.Beam.Json` convenience module with `beam`-pre-applied
  `auto`, `autoWith`, `validate`, `validateWith`, `validateMap`,
  `validateJson`, `dump`, `parseRaw`, `jsonSchemaOf`, `jsonSchemaOfCodec`.
