---
name: Fable.TypedJson.Beam
# last_commit_released will be set by ShipIt on the first published release.
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 0.1.0

### 🚀 Features

* Initial release: BEAM (Erlang) shim for `Fable.TypedJson`.
* `BeamBackend` implementing `IJsonBackend` via `Fable.Beam.Maps` and `Fable.Beam.Jsx.Jsx` (`jsx.decode` / `jsx.encode`).
* `Fable.TypedJson.Beam.Json` convenience module with `beam`-pre-applied
  `auto`, `autoWith`, `validate`, `validateWith`, `validateMap`,
  `validateJson`, `dump`, `parseRaw`, `jsonSchemaOf`, `jsonSchemaOfCodec`.
