"""Protect workload identities and the commands used for matching CodSpeed comparisons."""

from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SELECTOR = ROOT / "benchmarks/cli/select-codspeed.py"


class SelectionTests(unittest.TestCase):
    def select(self, *args):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "nested/selected.yml"
            result = subprocess.run(
                [sys.executable, str(SELECTOR), *args, "--output", str(output)],
                cwd=directory,
                capture_output=True,
                text=True,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            return output.read_text()

    def blocks(self, config):
        return config.split("  - name: ")[1:]

    def test_quick_full_preserves_canonical_config(self):
        self.assertEqual(self.select("--suite", "full"), (ROOT / "codspeed.yml").read_text())

    def test_suite_and_target_selection(self):
        for suite, per_target in [("core", 10), ("sequences", 4), ("repeatability", 3), ("full", 14)]:
            for target in ["all", "dotnet", "js", "python", "beam"]:
                with self.subTest(suite=suite, target=target):
                    config = self.select("--suite", suite, "--target", target)
                    blocks = self.blocks(config)
                    self.assertEqual(len(blocks), per_target * (4 if target == "all" else 1))
                    for block in blocks:
                        if target != "all":
                            self.assertTrue(block.startswith(target + "/"))
                        self.assertIn("  - name: " + block, (ROOT / "codspeed.yml").read_text())

    def test_repeatability_covers_disputed_hot_paths(self):
        blocks = self.blocks(self.select("--suite", "repeatability", "--target", "js"))
        self.assertEqual(
            {block.splitlines()[0].split("/")[1] for block in blocks},
            {"decode-record", "strict-int64-wide", "decode-array-128"},
        )

    def test_confirmation_names_match_commands_and_start_new_histories(self):
        canonical = {block.splitlines()[0]: block for block in self.blocks((ROOT / "codspeed.yml").read_text())}
        config = self.select("--suite", "full", "--profile", "confirm")
        self.assertIn("  min-rounds: 7\n  max-rounds: 7", config)
        blocks = self.blocks(config)
        self.assertEqual(len(blocks), 56)
        for block in blocks:
            name, exec_line = block.splitlines()
            target, version, scenario, count, warmup = name.split("/")
            self.assertEqual(version, "confirm-v1")
            args = shlex.split(exec_line.removeprefix("    exec: "))
            self.assertEqual(args, ["bash", "benchmarks/cli/run.sh", target, scenario, count[:-4], warmup[:-7]])
            original = f"{target}/{scenario}/{int(count[:-4]) // 3}-ops"
            self.assertIn(original, canonical)
            self.assertNotIn(name, canonical)
            self.assertEqual(int(args[-2]), int(shlex.split(canonical[original].splitlines()[1])[-1]) * 3)
            self.assertGreaterEqual(int(args[-1]), 0)

    def test_confirmation_warms_js_in_process(self):
        config = self.select("--target", "js", "--suite", "repeatability", "--profile", "confirm")
        self.assertIn("js decode-record 3000000 100000", config)
        self.assertIn("js strict-int64-wide 2400000 100000", config)
        self.assertIn("js decode-array-128 300000 10000", config)

    def test_throughput_uses_instrumented_entrypoint(self):
        config = self.select("--target", "js", "--suite", "repeatability", "--profile", "throughput")
        self.assertEqual(config, "benchmarks:\n  - entrypoint: node build/codspeed/js-throughput/bench.mjs\n")

    def test_throughput_rejects_unsupported_selections(self):
        for target, suite in [("all", "repeatability"), ("beam", "repeatability"), ("js", "core")]:
            with tempfile.TemporaryDirectory() as directory:
                output = Path(directory) / "selected.yml"
                result = subprocess.run(
                    [sys.executable, str(SELECTOR), "--profile", "throughput", "--target", target,
                     "--suite", suite, "--output", str(output)],
                    capture_output=True, text=True,
                )
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("throughput requires", result.stderr)
                self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
