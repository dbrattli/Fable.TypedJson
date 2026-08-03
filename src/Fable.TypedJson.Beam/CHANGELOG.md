---
last_commit_released: 0375e30f696c8f2b116a164a8991333e84457993
name: Fable.TypedJson.Beam
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 5.0.0 - 2026-08-03

### 🚀 Features

* $ref/$defs schema mode, schema IR access, and DateTime/Guid/decimal support (#49) ([0375e30](https://github.com/dbrattli/Fable.TypedJson/commit/0375e30f696c8f2b116a164a8991333e84457993))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/d34e1f4c55505495b907aace4de33c34b73822a3..0375e30f696c8f2b116a164a8991333e84457993)</small></strong>

## 5.0.0-rc.1 - 2026-08-02

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/33b94f4e2dfde262fad19673e4bdcb03f3a070fd..d34e1f4c55505495b907aace4de33c34b73822a3)</small></strong>

## 0.4.1 - 2026-07-18

### 🐞 Bug Fixes

* *(beam)* Support Fable 5.11 ref-wrapped union case values (#31) ([d9bf688](https://github.com/dbrattli/Fable.TypedJson/commit/d9bf6882a3340d9445285b5e5ec459e6716b2036))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/8f6e40d622209bce15ce75ff6c22f3e4c7c4454c..33b94f4e2dfde262fad19673e4bdcb03f3a070fd)</small></strong>

## 0.4.0 - 2026-05-21

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/fff65d7af324f670816212bbd6d4efa7db712b1e..8f6e40d622209bce15ce75ff6c22f3e4c7c4454c)</small></strong>

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
