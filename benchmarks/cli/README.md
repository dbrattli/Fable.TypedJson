# CodSpeed benchmarks

The fourteen workloads in [Main.fs](Main.fs) compile to .NET, JavaScript,
Python, and BEAM. [codspeed.yml](../../codspeed.yml) runs the prebuilt programs
as CLI commands and reports walltime under separate names for each runtime.

## Local use

```sh
just restore
just build-bench-cli
just bench-cli-smoke
just bench-cli dotnet decode-record 1000000
```

Individual builds are available as `just build-bench-cli-dotnet`,
`build-bench-cli-js`, `build-bench-cli-python`, and `build-bench-cli-beam`.
The smoke command checks every workload on every runtime with three operations.
Unknown workloads, invalid iteration counts, or incorrect results fail the command.

After installing the [CodSpeed CLI](https://codspeed.io/docs/benchmarks/cli-commands),
authenticate with `codspeed auth login`, then run `codspeed run -m walltime` to
measure and upload the configured workloads. On Linux, CodSpeed's kernel setup
requires sudo credentials; run `sudo -v` in the same terminal first if necessary.
For a focused measurement, use:

```sh
codspeed exec -m walltime --name local/dotnet/decode-record/1000000-ops -- \
    bash benchmarks/cli/run.sh dotnet decode-record 1000000
```

Compare local results on the same machine. CI uses a fixed ARM64 macro runner;
its absolute timings are not directly comparable with a local machine.

## What the numbers mean

Each result is the elapsed time of a **whole fixed batch**, including process
startup, setup, warmup operations, and output.
Compilation and dependency restoration happen beforehand. Codecs are reused,
and decode-only workloads reuse parsed input. Only `parse-decode-record`
parses inside the repeated loop; `encode-record` produces JSON text.
`construct-record` creates a codec and decodes one pre-parsed record per operation,
separating repeated construction from cached decoding. The original seven workloads
retain 10,000 warmup operations; the seven structural workloads use ten so that
recursive plan construction and quadratic traversal do not dominate warmup.

Every operation checks its result and contributes to a checked checksum.
The encode fixture also round-trips before its loop. Counts are fixed rather
than adjusted dynamically: a slower version must execute the same amount of
work. Counts were reduced tenfold after Graviton measurements exceeded the
30-minute job limit. CodSpeed performs a command warmup followed by three
measurement rounds. These counts start a new baseline and cannot be compared
with results from the previous larger batches.

| Workload | What it exercises | .NET operations | JS operations | Python operations | BEAM operations |
| --- | --- | ---: | ---: | ---: | ---: |
| `decode-record` | Cached codec, pre-parsed three-field record | 600,000 | 1,000,000 | 50,000 | 100,000 |
| `parse-decode-record` | JSON parsing plus record decoding | 200,000 | 500,000 | 30,000 | 50,000 |
| `encode-record` | Record serialization to JSON text | 500,000 | 1,000,000 | 50,000 | 100,000 |
| `strict-int` | Valid Int32 value, 42 | 2,000,000 | 2,000,000 | 50,000 | 200,000 |
| `strict-int64-small` | Int64 value within Int32 bounds, 42 | 2,000,000 | 2,000,000 | 50,000 | 200,000 |
| `strict-int64-wide` | Int64 value outside Int32 bounds, 2147483648 | 2,000,000 | 800,000 | 50,000 | 200,000 |
| `strict-int-reject` | Out-of-range Int32 rejection with field path | 500,000 | 200,000 | 10,000 | 50,000 |
| `construct-record` | Codec construction plus one checked record decode | 300 | 50,000 | 500 | 2,000 |
| `decode-wide-record` | Cached codec, pre-parsed 16-field record | 200,000 | 500,000 | 10,000 | 20,000 |
| `decode-array-128` | 128 integers decoded to an F# array | 25,000 | 100,000 | 2,000 | 10,000 |
| `decode-array-1024` | 1,024 integers decoded to an F# array | 2,500 | 10,000 | 250 | 250 |
| `decode-list-1024` | 1,024 integers decoded to an F# list | 1,500 | 20,000 | 250 | 1,000 |
| `decode-recursive-tree` | Fifteen nodes of a recursive record/list type | 50 | 5,000 | 200 | 200 |
| `reject-nested-sequence` | Strict nested rejection at `groups[1].items[2]` | 200,000 | 50,000 | 10,000 | 20,000 |

Sequence and tree workloads sum all decoded values; wide-record decoding checks
all sixteen fields. These checks are included in the measured batch. Array sizes
expose traversal scaling, while array/list outputs exercise different builders.
Structural counts are calibrated from the first Graviton run. In particular, the
.NET construction and recursive-tree batches previously took 33 and 57 seconds
per round. Reduced counts have new benchmark names; the original seven workload
counts remain unchanged.

Compare a workload against its own history. These are not isolated nanoseconds
per decode, and the different batch sizes prevent comparing raw times across
runtimes. The wide Int64 JSON value follows the backend's native numeric
representation, which differs across targets. Keep iteration counts, runtime
versions, and the CI runner label
stable when assessing a code change. Counts are included in benchmark names
so changing the amount of work starts a new history.

The .NET runner disables tiered compilation, and BEAM uses one scheduler.
Those settings are fixed in [run.sh](run.sh). CLI timing tracks elapsed time;
use `just bench` for BenchmarkDotNet's .NET allocation diagnostics.

## CI and authentication

CI defaults to 40 workloads (ten per runtime). `strict-int`,
`strict-int64-small`, `decode-wide-record`, and `reject-nested-sequence` are
reserved for explicit `full` runs: correctness remains covered by the shared
tests, while the core suite retains wide Int64, scalar rejection, and both
sequence output builders. Both suites use three measurement rounds.

Manual dispatch accepts `target` (`all`, `dotnet`, `js`, `python`, `beam`) and
`suite` (`core`, `full`). Focused runs build, install, and measure only that
runtime:

```sh
gh workflow run codspeed.yml --ref <branch> -f target=beam -f suite=core
```

[select-codspeed.py](select-codspeed.py) filters the canonical configuration;
it writes the selected config into the build artifact. For the same selection
locally:

```sh
python3 benchmarks/cli/select-codspeed.py --target beam --suite core --output .codspeed-selected.yml
codspeed run -m walltime --config .codspeed-selected.yml
```

Plain `codspeed run -m walltime` still measures all 56 configured workloads.

[The workflow](../../.github/workflows/codspeed.yml) runs on pushes to `main`,
pull requests labeled `perf`, and manual dispatch. Adding `perf` starts a run;
subsequent commits rerun benchmarks while the label remains. Unlabeled PRs,
including Dependabot PRs, skip the benchmark job without allocating a runner.
Pushes to `main` maintain the comparison baseline, except changes limited to
Markdown files or `docs/`. PR runs still require `perf`, including documentation
PRs explicitly selected for measurement.

Compilation and the first smoke check run on GitHub's `ubuntu-22.04-arm` runner.
A tar artifact preserves the generated files and BEAM symbolic links. The
Graviton macro job downloads those prebuilt programs, installs .NET 10,
Node.js 20, Python 3.12 with its locked dependencies, and Erlang/OTP 27, then
checks and measures the programs. It does not restore .NET dependencies,
transpile F#, or compile Erlang. All four targets report through the same workflow.

Authentication uses [OIDC](https://codspeed.io/docs/integrations/ci/github-actions/configuration#authentication),
with `contents: read` and `id-token: write` scoped to the benchmark job.
No static `CODSPEED_TOKEN` secret is needed. The repository must be connected to
CodSpeed.

The `fable-hub` organization must enable CodSpeed for this repository and allow
public repositories in the runner group used by CodSpeed. See the
[macro runner prerequisites](https://codspeed.io/docs/integrations/ci/github-actions/macro-runners#prerequisites).
Keep the Graviton runner label stable for comparable measurements. Switching
from the previous GitHub-hosted x64 runner establishes a new ARM64 baseline;
times across that transition do not measure a code regression or improvement.
