"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

test("manifest includes OpenDeck's required action state and encoder declaration", () => {
  const folder = path.join(__dirname, "..", "net.parrec.deck.windows-essentials.sdPlugin");
  const manifest = JSON.parse(fs.readFileSync(path.join(folder, "manifest.json"), "utf8"));
  const action = manifest.Actions[0];
  assert.equal(manifest.CodePath, "bin/plugin.cjs");
  assert.ok(action.Controllers.includes("Encoder"));
  assert.ok(Array.isArray(action.States) && action.States.length > 0);
  assert.ok(fs.existsSync(path.join(folder, `${action.States[0].Image}.svg`)));
});

test("manifest exposes the basic media controls as keypad actions", () => {
  const folder = path.join(__dirname, "..", "net.parrec.deck.windows-essentials.sdPlugin");
  const manifest = JSON.parse(fs.readFileSync(path.join(folder, "manifest.json"), "utf8"));
  const mediaActions = manifest.Actions.filter((action) => action.UUID.includes(".media-"));
  assert.deepEqual(mediaActions.map((action) => action.UUID), [
    "net.parrec.deck.windows-essentials.media-play-pause",
    "net.parrec.deck.windows-essentials.media-previous",
    "net.parrec.deck.windows-essentials.media-next"
  ]);
  assert.ok(mediaActions.every((action) => action.Controllers.includes("Keypad")));
  const playPause = mediaActions.find((action) => action.UUID.endsWith("media-play-pause"));
  assert.equal(playPause.Icon, "imgs/media-toggle-icon");
  assert.equal(playPause.DisableAutomaticStates, true);
  assert.deepEqual(playPause.States.map((state) => state.Image), ["imgs/play-pause-icon", "imgs/pause-icon"]);
});

test("manifest exposes microphone volume as an encoder action", () => {
  const folder = path.join(__dirname, "..", "net.parrec.deck.windows-essentials.sdPlugin");
  const manifest = JSON.parse(fs.readFileSync(path.join(folder, "manifest.json"), "utf8"));
  const microphone = manifest.Actions.find((action) => action.UUID.endsWith(".microphone-volume"));
  assert.deepEqual(microphone.Controllers, ["Encoder"]);
  assert.equal(microphone.States[0].Image, "imgs/microphone-icon");
  assert.ok(fs.existsSync(path.join(folder, "imgs", "microphone-icon.svg")));
});

test("manifest exposes configurable audio-output action", () => {
  const folder = path.join(__dirname, "..", "net.parrec.deck.windows-essentials.sdPlugin");
  const manifest = JSON.parse(fs.readFileSync(path.join(folder, "manifest.json"), "utf8"));
  const output = manifest.Actions.find((action) => action.UUID.endsWith(".audio-output"));
  assert.deepEqual(output.Controllers, ["Keypad"]);
  assert.equal(output.PropertyInspectorPath, "propertyInspector/audio-output.html");
  assert.ok(fs.existsSync(path.join(folder, output.PropertyInspectorPath)));
});

test("manifest exposes a keypad action to lock the PC", () => {
  const folder = path.join(__dirname, "..", "net.parrec.deck.windows-essentials.sdPlugin");
  const manifest = JSON.parse(fs.readFileSync(path.join(folder, "manifest.json"), "utf8"));
  const lockAction = manifest.Actions.find((action) => action.UUID.endsWith(".lock-pc"));
  assert.deepEqual(lockAction.Controllers, ["Keypad"]);
  assert.equal(lockAction.States[0].Image, "imgs/lock-icon");
});
