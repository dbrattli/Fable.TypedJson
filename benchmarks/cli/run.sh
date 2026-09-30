#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
target="${1:?Usage: run.sh <dotnet|js|python|beam> <scenario> <iterations> | --smoke}"
shift

# decision: execs prebuilt runners so dependency restoration and compilation never enter the measured command
case "$target" in
    dotnet)
        export DOTNET_TieredCompilation=0
        exec dotnet "$repo_root/build/codspeed/dotnet/Fable.TypedJson.Benchmark.DotNet.dll" "$@"
        ;;
    js)
        exec node "$repo_root/build/codspeed/js/Main.js" "$@"
        ;;
    python)
        exec "$repo_root/.venv/bin/python" "$repo_root/build/codspeed/python/main.py" "$@"
        ;;
    beam)
        cd "$repo_root/build/codspeed/beam"
        # decision: fixes the scheduler count because the workloads run synchronously in one process
        exec erl +S 1:1 -noshell -pa _build/default/lib/*/ebin \
            -eval 'main:main(init:get_plain_arguments()).' -extra "$@"
        ;;
    *)
        echo "Unknown target: $target" >&2
        exit 1
        ;;
esac
