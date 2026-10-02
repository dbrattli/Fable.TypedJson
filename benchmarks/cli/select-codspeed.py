"""Select CI workloads without adding a YAML dependency to the measurement runner."""

import argparse
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--target", choices=["all", "dotnet", "js", "python", "beam"], default="all")
parser.add_argument("--suite", choices=["core", "sequences", "full"], default="core")
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()

# decision: retains duplicate scalar cases and additional structure probes for explicit full runs
extra_scenarios = {"strict-int", "strict-int64-small", "decode-wide-record", "reject-nested-sequence"}
sequence_scenarios = {"decode-array-128", "decode-array-1024", "decode-list-1024", "decode-recursive-tree"}
source = Path(__file__).resolve().parents[2] / "codspeed.yml"
# invariant: the canonical configuration has a shared preamble and literal name/exec blocks
preamble, *blocks = source.read_text().split("  - name: ")
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
    selected.append("  - name: " + block)

if not selected:
    parser.error("selection contains no benchmarks")
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(preamble + "".join(selected))
print(f"Selected {len(selected)} {args.suite} workloads for {args.target}")
