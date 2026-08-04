---
last_commit_released: 02b1f4805966b8314b4ef0f23a9a4ef38ef3ddaf
name: Fable.TypedJson.JS
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 5.1.0 - 2026-08-04

### 🚀 Features

* Case rules on the Map<string,string> validation path (#53) ([bc9c2b9](https://github.com/dbrattli/Fable.TypedJson/commit/bc9c2b95e7ee7a98c51ac19abe92c26cdb16a2aa))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/0375e30f696c8f2b116a164a8991333e84457993..02b1f4805966b8314b4ef0f23a9a4ef38ef3ddaf)</small></strong>

## 5.0.0 - 2026-08-03

### 🚀 Features

* $ref/$defs schema mode, schema IR access, and DateTime/Guid/decimal support (#49) ([0375e30](https://github.com/dbrattli/Fable.TypedJson/commit/0375e30f696c8f2b116a164a8991333e84457993))

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/d34e1f4c55505495b907aace4de33c34b73822a3..0375e30f696c8f2b116a164a8991333e84457993)</small></strong>

## 5.0.0-rc.1 - 2026-08-02

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/8f6e40d622209bce15ce75ff6c22f3e4c7c4454c..d34e1f4c55505495b907aace4de33c34b73822a3)</small></strong>

## 0.4.0 - 2026-05-21

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/fff65d7af324f670816212bbd6d4efa7db712b1e..8f6e40d622209bce15ce75ff6c22f3e4c7c4454c)</small></strong>

## 0.3.0 - 2026-05-03

<strong><small>[View changes on Github](https://github.com/dbrattli/Fable.TypedJson/compare/6d52d3074b3a8fde66276ac5540ecd0cd1f08cd6..fff65d7af324f670816212bbd6d4efa7db712b1e)</small></strong>

## 0.2.0 - 2026-05-03

### 🚀 Features

* Add JavaScript backend shim (Fable.TypedJson.JS) (#4) ([df883bb](https://github.com/dbrattli/Fable.TypedJson/commit/df883bb1caf6781d6cbd2733e2d63453f7afd207))

## 0.1.0

### 🚀 Features

* Initial release: JavaScript shim for `Fable.TypedJson`.
* `JSBackend` implementing `IJsonBackend` via native `JSON.parse` / `JSON.stringify` and JS object/array primitives.
* `Fable.TypedJson.JS.Json` convenience module with `js`-pre-applied
  `auto`, `autoWith`, `validate`, `validateWith`, `validateMap`,
  `validateJson`, `dump`, `parseRaw`, `jsonSchemaOf`, `jsonSchemaOfCodec`.
