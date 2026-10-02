"""Select CI workloads without adding a YAML dependency to the measurement runner."""

import argparse
from pathlib import Path
import shlex

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--target", choices=["all", "dotnet", "js", "python", "beam"], default="all")
parser.add_argument("--suite", choices=["core", "sequences", "repeatability", "full"], default="core")
parser.add_argument("--profile", choices=["quick", "confirm"], default="quick")
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()

# decision: retains duplicate scalar cases and additional structure probes for explicit full runs
extra_scenarios = {"strict-int", "strict-int64-small", "decode-wide-record", "reject-nested-sequence"}
sequence_scenarios = {"decode-array-128", "decode-array-1024", "decode-list-1024", "decode-recursive-tree"}
# decision: calibrates scalar, record, and traversal measurements with a small fixed workload set
repeatability_scenarios = {"decode-record", "strict-int64-wide", "decode-array-128"}
structural_scenarios = sequence_scenarios | {"construct-record", "decode-wide-record", "reject-nested-sequence"}
source = Path(__file__).resolve().parents[2] / "codspeed.yml"
# invariant: the canonical configuration has a shared preamble and literal name/exec blocks
preamble, *blocks = source.read_text().split("  - name: ")
if args.profile == "confirm":
    # decision: spends extra rounds only on explicit confirmation runs to bound routine CI cost
    preamble = """# confirm-v1: threefold batches and seven rounds; names include the in-process warmup count.
options:
  warmup-time: 1s
  min-rounds: 7
  max-rounds: 7

benchmarks:
"""
selected = []
for block in blocks:
    name = block.splitlines()[0]
    target, scenario, _ = name.split("/")
    if args.target != "all" and target != args.target:
        continue
    if args.suite == "core" and scenario in extra_scenarios:
        continue
    if args.suite == "sequences" and scenario not in sequence_scenarios:
        continue
    if args.suite == "repeatability" and scenario not in repeatability_scenarios:
        continue
    if args.profile == "confirm":
        lines = block.splitlines()
        command = shlex.split(lines[1].removeprefix("    exec: "))
        if len(command) != 5 or command[:4] != ["bash", "benchmarks/cli/run.sh", target, scenario]:
            parser.error(f"unsupported canonical command for {name}")
        count = int(command[-1]) * 3
        # decision: warms JS longer within each process because fresh command warmups cannot preserve V8's optimized code
        if target == "js":
            warmup = 10000 if scenario in sequence_scenarios or scenario == "reject-nested-sequence" else 100000
            if scenario in {"construct-record", "decode-recursive-tree"}:
                warmup = 50
        else:
            warmup = 10 if scenario in structural_scenarios else 10000
        # invariant: confirmation names encode changed counts and warmup so they cannot compare against quick histories
        block = (
            f"{target}/confirm-v1/{scenario}/{count}-ops/{warmup}-warmup\n"
            f"    exec: bash benchmarks/cli/run.sh {target} {scenario} {count} {warmup}\n"
        )
    selected.append("  - name: " + block)

if not selected:
    parser.error("selection contains no benchmarks")
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(preamble + "".join(selected))
print(f"Selected {len(selected)} {args.suite} workloads for {args.target} ({args.profile})")
