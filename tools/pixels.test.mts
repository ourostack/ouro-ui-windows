// The words check against real rows from CI's contrast-theme screenshots: before the fix (WinUI's backplate drew a
// black box with white words on the highlight fill) and after it. fixtures/contrast-rows.json names both runs.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import { wordsAsReported } from "./pixels.mts";

const rows = JSON.parse(await readFile(new URL("./fixtures/contrast-rows.json", import.meta.url), "utf8"));
const check = (r: { fill: string; label: string; behind: string; line: string[] }) => wordsAsReported(r.line, r.fill.startsWith("#00") ? r.behind : r.fill, r.label);

test("it catches the contrast backplate on solid Buttons and Badges", () => {
  for (const id of ["action.add_shopping_list_item", "3"]) assert.equal(check(rows.before[id]).ok, false, id);
});

test("it passes the same controls once they draw as they report", () => {
  for (const id of ["action.add_shopping_list_item", "3"]) assert.equal(check(rows.after[id]).ok, true, `${id}: ${check(rows.after[id]).why}`);
});

test("it passes controls the backplate never touched, before and after", () => {
  for (const when of ["before", "after"]) for (const id of ["action.place_grocery_order", "Vegan"]) assert.equal(check(rows[when][id]).ok, true, `${when} ${id}: ${check(rows[when][id]).why}`);
});

test("a row with no ink fails: the words weren't drawn", () => {
  assert.equal(wordsAsReported(["#FF8EE3F0", "#FF8EE3F0"], "#FF8EE3F0", "#FF263B50").ok, false);
});
