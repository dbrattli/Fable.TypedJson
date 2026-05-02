// Tiny test runner for the Fable-transpiled F# test suite (JavaScript target).
//
// Walks `build/js_test/Test*.js`, imports each module, and invokes every
// exported function whose name starts with `test`. Fable mangles F# function
// names like `` `test foo bar` `` to `test$0020foo$0020bar`, so the prefix
// match catches them all.
//
// Pass: function returns without throwing.
// Fail: any thrown exception. The test helpers in test/Testing.fs throw on
// inequality (so `equal` failures show up as JS Errors).
//
// Usage (from repo root):
//   node test_js/runner.mjs

import { readdirSync } from "node:fs";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const TESTS_DIR = resolve("build/js_test");
const RED = "\x1b[31m";
const GREEN = "\x1b[32m";
const RESET = "\x1b[0m";
const TICK = `${GREEN}✓${RESET}`;
const CROSS = `${RED}✗${RESET}`;

const moduleFiles = readdirSync(TESTS_DIR)
    .filter((f) => f.startsWith("Test") && f.endsWith(".js"))
    .sort();

let pass = 0;
let fail = 0;

for (const file of moduleFiles) {
    const moduleName = file.replace(/\.js$/, "");
    const url = pathToFileURL(join(TESTS_DIR, file)).href;
    const mod = await import(url);

    const testNames = Object.keys(mod)
        .filter((k) => k.startsWith("test") && typeof mod[k] === "function")
        .sort();

    if (testNames.length === 0) continue;

    console.log(`--- ${moduleName} ---`);

    for (const name of testNames) {
        // Demangle Fable's `$0020` (space) so output is readable
        const display = name.replace(/\$0020/g, " ");
        try {
            mod[name]();
            console.log(`  ${TICK} ${display}`);
            pass++;
        } catch (err) {
            console.log(`  ${CROSS} ${display}`);
            console.log(`    ${err && err.message ? err.message : err}`);
            fail++;
        }
    }
}

const total = pass + fail;
console.log(`\n=== Results ===`);
console.log(`Total: ${total} | Passed: ${pass} | Failed: ${fail}`);

if (fail > 0) {
    console.log("\nSome tests FAILED!");
    process.exit(1);
} else {
    console.log("\nAll tests passed!");
}
