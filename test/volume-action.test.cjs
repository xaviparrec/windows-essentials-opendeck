"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const { MasterVolumeAction } = require("../net.parrec.deck.windows-essentials.sdPlugin/bin/volume-action.cjs");

test("rotation uses the returned Windows audio state for feedback", async () => {
  const calls = []; const titles = [];
  const states = [{ level: 73, muted: false }, { level: 69, muted: false }];
  const action = new MasterVolumeAction({ adjust: async (...args) => { calls.push(args); return states.shift(); } }, (...args) => titles.push(args));
  await action.rotate("dial-1", 3); await action.rotate("dial-1", -2);
  assert.deepEqual(calls, [[3], [-2]]);
  assert.equal(titles.length, 2);
  assert.deepEqual(titles[0][1], { title: "Master volume", value: "73%", indicator: 73 });
  assert.deepEqual(titles[1][1], { title: "Master volume", value: "69%", indicator: 69 });
});

test("encoder press uses Windows' actual mute state", async () => {
  const calls = []; const titles = [];
  const states = [{ level: 50, muted: true }, { level: 50, muted: false }];
  const action = new MasterVolumeAction({ toggleMute: async (...args) => { calls.push(args); return states.shift(); } }, (...args) => titles.push(args));
  await action.press("dial-1"); await action.press("dial-1");
  assert.deepEqual(calls, [[], []]);
  assert.deepEqual(titles[0][1], { title: "Muted", value: "Muted", indicator: 0 });
  assert.deepEqual(titles[1][1], { title: "Master volume", value: "50%", indicator: 50 });
});

test("microphone feedback uses its own labels", () => {
  const action = new MasterVolumeAction({}, () => {}, "Microphone", "Mic muted");
  assert.deepEqual(action.feedback({ level: 64, muted: false }), { title: "Microphone", value: "64%", indicator: 64 });
  assert.deepEqual(action.feedback({ level: 64, muted: true }), { title: "Mic muted", value: "Muted", indicator: 0 });
});
