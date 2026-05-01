---
name: Fable.TypedJson.Python
# last_commit_released will be set by ShipIt on the first published release.
---

# Changelog

All notable changes to this project will be documented in this file.

This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This changelog is generated using [EasyBuild.ShipIt](https://github.com/easybuild-org/EasyBuild.ShipIt).

⚠ Only edit the front matter metadata at the top of this file. All other changes will be overwritten when a new release is created.

## 0.1.0

### 🚀 Features

* Initial release: Python shim for `Fable.TypedJson`.
* `PythonBackend` implementing `IJsonBackend` via `Fable.Python.Json` (`json.loads` / `json.dumps`) and `Fable.Core.PyInterop`.
* Wraps native Python `int` / `float` as Fable's `int32` / `float64` at the read boundary so erased `JInt` / `JFloat` patterns dispatch correctly.
* `Fable.TypedJson.Python.Json` convenience module with `python`-pre-applied
  `auto`, `autoWith`, `validate`, `validateWith`, `validateMap`,
  `validateJson`, `dump`, `parseRaw`, `jsonSchemaOf`, `jsonSchemaOfCodec`.
