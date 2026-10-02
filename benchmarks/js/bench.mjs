import { getCodspeedRunnerMode } from "@codspeed/core";
import { withCodSpeed } from "@codspeed/tinybench-plugin";
import { Bench } from "tinybench";
import { prepareScenario } from "./Main.js";

const batchSize = 1000;
const scenarios = ["decode-record", "strict-int64-wide", "decode-array-128"];
const args = process.argv.slice(2);
const smoke = args.length === 1 && args[0] === "--smoke";
if (args.length !== 0 && !smoke) {
    throw new Error("Usage: bench.mjs [--smoke]");
}

// decision: prepares codecs and input before tinybench's warmup and measured loops
const batches = scenarios.map((name) => {
    const operation = prepareScenario(name);
    const run = () => {
        let checksum = 0;
        for (let i = 0; i < batchSize; i++) {
            checksum += operation();
        }
        // invariant: every operation validates its result and the complete batch must contribute to this checksum
        if (checksum !== batchSize) {
            throw new Error(`Incorrect ${name} batch: ${checksum}`);
        }
        return checksum;
    };
    return { name, run };
});

if (smoke) {
    for (const batch of batches) {
        batch.run();
    }
    console.log(`Checked ${batches.length} JS throughput batches`);
} else {
    if (getCodspeedRunnerMode() === "disabled") {
        throw new Error("Run measurements through codspeed run, or use --smoke for correctness checks");
    }
    // decision: measures repeated fixed batches in one warmed process; the plugin excludes setup and warmup
    const bench = withCodSpeed(new Bench({
        name: "js-throughput-v1",
        time: 2000,
        iterations: 100,
        warmupTime: 1000,
        warmupIterations: 100,
        throws: true,
    }));
    for (const batch of batches) {
        bench.add(`${batch.name}/${batchSize}-ops`, batch.run);
    }
    await bench.run();
}
