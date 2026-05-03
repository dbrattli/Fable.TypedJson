---
last_commit_released: 6d52d3074b3a8fde66276ac5540ecd0cd1f08cd6
name: Fable.TypedJson.JS
force_version: 0.3.0
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

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
