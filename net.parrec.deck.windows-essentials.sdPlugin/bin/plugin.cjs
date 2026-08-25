"use strict";

const crypto = require("node:crypto");
const net = require("node:net");
const path = require("node:path");
const { spawn } = require("node:child_process");
const { MasterVolumeAction } = require("./volume-action.cjs");

const MASTER_VOLUME_ACTION_UUID = "net.parrec.deck.windows-essentials.master-volume";
const MICROPHONE_VOLUME_ACTION_UUID = "net.parrec.deck.windows-essentials.microphone-volume";
const OUTPUT_SWITCH_ACTION_UUID = "net.parrec.deck.windows-essentials.audio-output";
const OUTPUT_SELECTOR_ACTION_UUID = "net.parrec.deck.windows-essentials.audio-output-selector";
const APP_VOLUME_ACTION_UUID = "net.parrec.deck.windows-essentials.app-volume";
const PLAY_PAUSE_ACTION_UUID = "net.parrec.deck.windows-essentials.media-play-pause";
const MEDIA_ACTIONS = new Map([
  ["net.parrec.deck.windows-essentials.media-previous", "previous"],
  ["net.parrec.deck.windows-essentials.media-next", "next"]
]);
const SYSTEM_ACTIONS = new Map([
  ["net.parrec.deck.windows-essentials.lock-pc", "lock-pc"],
  ["net.parrec.deck.windows-essentials.sleep-pc", "sleep-pc"],
  ["net.parrec.deck.windows-essentials.restart-pc", "restart-pc"],
  ["net.parrec.deck.windows-essentials.shutdown-pc", "shutdown-pc"]
]);
const shutdownConfirmations = new Map();

function argument(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

/** Minimal dependency-free WebSocket client for the legacy Stream Deck/OpenDeck protocol. */
class StreamDeckSocket {
  constructor(port, pluginUUID, registerEvent, info) {
    this.port = port;
    this.pluginUUID = pluginUUID;
    this.registerEvent = registerEvent;
    this.info = info;
    this.buffer = Buffer.alloc(0);
    this.handshaken = false;
  }

  connect(onEvent) {
    this.socket = net.createConnection({ host: "127.0.0.1", port: this.port });
    this.socket.on("connect", () => this.handshake());
    this.socket.on("data", (chunk) => this.receive(chunk, onEvent));
    this.socket.on("error", (error) => console.error("OpenDeck connection error:", error.message));
    this.socket.on("close", () => console.error("OpenDeck connection closed"));
  }

  handshake() {
    const key = crypto.randomBytes(16).toString("base64");
    this.socket.write([
      "GET / HTTP/1.1", `Host: 127.0.0.1:${this.port}`, "Upgrade: websocket",
      "Connection: Upgrade", `Sec-WebSocket-Key: ${key}`, "Sec-WebSocket-Version: 13", "", ""
    ].join("\r\n"));
  }

  receive(chunk, onEvent) {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    if (!this.handshaken) {
      const headerEnd = this.buffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) return;
      const header = this.buffer.subarray(0, headerEnd).toString("utf8");
      if (!header.startsWith("HTTP/1.1 101")) throw new Error(`WebSocket handshake failed: ${header}`);
      this.handshaken = true;
      this.buffer = this.buffer.subarray(headerEnd + 4);
      this.send({ event: this.registerEvent, uuid: this.pluginUUID });
    }
    while (this.buffer.length >= 2) {
      const first = this.buffer[0];
      let length = this.buffer[1] & 0x7f;
      let offset = 2;
      if (length === 126) {
        if (this.buffer.length < 4) return;
        length = this.buffer.readUInt16BE(2); offset = 4;
      } else if (length === 127) {
        if (this.buffer.length < 10) return;
        length = Number(this.buffer.readBigUInt64BE(2)); offset = 10;
      }
      if (this.buffer.length < offset + length) return;
      const payload = this.buffer.subarray(offset, offset + length);
      this.buffer = this.buffer.subarray(offset + length);
      const opcode = first & 0x0f;
      if (opcode === 0x9) this.writeFrame(payload, 0xA);
      if (opcode === 0x1) onEvent(JSON.parse(payload.toString("utf8")));
    }
  }

  send(value) { this.writeFrame(Buffer.from(JSON.stringify(value))); }

  writeFrame(payload, opcode = 0x1) {
    const mask = crypto.randomBytes(4);
    const header = payload.length < 126 ? Buffer.from([0x80 | opcode, 0x80 | payload.length]) : Buffer.from([0x80 | opcode, 0x80 | 126, payload.length >> 8, payload.length & 0xff]);
    const masked = Buffer.from(payload);
    for (let index = 0; index < masked.length; index += 1) masked[index] ^= mask[index % 4];
    this.socket.write(Buffer.concat([header, mask, masked]));
  }
}

class WindowsEndpointVolume {
  constructor() {
    this.helper = path.join(__dirname, "audio-helper", "AudioEndpointHelper.exe");
    this.pending = [];
    this.buffer = "";
    this.child = undefined;
  }

  get() { return this.run("get"); }
  // Media keys deliberately retain Windows' native volume flyout/overlay.
  // The helper reads the endpoint afterwards, so feedback stays exact.
  adjust(ticks) { return this.run("media", ticks > 0 ? "up" : "down", String(Math.abs(ticks))); }
  toggleMute() { return this.run("media", "mute", "1"); }
  sendMediaKey(key) { return this.run("key", key); }
  getPlaybackState() { return this.run("media-state"); }
  togglePlayback() { return this.run("media-toggle"); }
  getMicrophone() { return this.run("mic-get"); }
  adjustMicrophone(ticks) { return this.run("mic-adjust", String(ticks)); }
  toggleMicrophoneMute() { return this.run("mic-toggle-mute"); }
  listOutputs() { return this.run("list-outputs"); }
  getDefaultOutput() { return this.run("get-default-output"); }
  setOutput(id) { return this.run("set-output", id); }
  cycleOutput(ticks) { return this.run("cycle-output", String(ticks)); }
  listApps() { return this.run("list-apps"); }
  getAppVolume(pid) { return this.run("app-get", String(pid)); }
  adjustAppVolume(pid, ticks) { return this.run("app-adjust", String(pid), String(ticks)); }
  toggleAppMute(pid) { return this.run("app-toggle-mute", String(pid)); }

  lockWorkstation() {
    return new Promise((resolve, reject) => {
      const child = spawn("rundll32.exe", ["user32.dll,LockWorkStation"], { windowsHide: true, stdio: "ignore" });
      child.on("error", reject);
      child.on("close", (code) => code === 0 ? resolve() : reject(new Error(`Windows lock command exited with code ${code}.`)));
    });
  }

  power(command) {
    const commands = {
      "sleep-pc": ["rundll32.exe", ["powrprof.dll,SetSuspendState", "0,1,0"]],
      "restart-pc": ["shutdown.exe", ["/r", "/t", "0"]],
      "shutdown-pc": ["shutdown.exe", ["/s", "/t", "0"]]
    };
    const [executable, args] = commands[command] || [];
    if (!executable) return Promise.reject(new Error(`Unknown power command: ${command}`));
    return new Promise((resolve, reject) => {
      const child = spawn(executable, args, { windowsHide: true, stdio: "ignore" });
      child.on("error", reject);
      child.on("close", (code) => code === 0 ? resolve() : reject(new Error(`Windows power command exited with code ${code}.`)));
    });
  }

  run(command, ...values) {
    this.start();
    return new Promise((resolve, reject) => {
      this.pending.push({ resolve, reject });
      this.child.stdin.write([command, ...values].join(" ") + "\n");
    });
  }

  start() {
    if (this.child && !this.child.killed) return;
    this.child = spawn(this.helper, ["serve"], { windowsHide: true, stdio: ["pipe", "pipe", "pipe"] });
    this.child.stdout.setEncoding("utf8");
    this.child.stdout.on("data", (chunk) => this.receive(chunk));
    this.child.on("error", (error) => this.stop(error));
    this.child.on("close", () => this.stop(new Error("Windows audio helper stopped.")));
    process.once("exit", () => this.child?.kill());
  }

  receive(chunk) {
    this.buffer += chunk;
    let newline;
    while ((newline = this.buffer.indexOf("\n")) >= 0) {
      const line = this.buffer.slice(0, newline).trim();
      this.buffer = this.buffer.slice(newline + 1);
      if (!line) continue;
      const pending = this.pending.shift();
      if (!pending) continue;
      try {
        const response = JSON.parse(line);
        if (response.error) pending.reject(new Error(response.error));
        else pending.resolve(response);
      } catch (error) {
        pending.reject(error);
      }
    }
  }

  stop(error) {
    const pending = this.pending.splice(0);
    this.child = undefined;
    for (const request of pending) request.reject(error);
  }

}

const port = Number(argument("-port"));
const pluginUUID = argument("-pluginUUID");
const registerEvent = argument("-registerEvent");
const info = argument("-info");
if (!port || !pluginUUID || !registerEvent) throw new Error("OpenDeck/Stream Deck launch arguments are missing.");

const socket = new StreamDeckSocket(port, pluginUUID, registerEvent, info);
const audio = new WindowsEndpointVolume();
const displayFeedback = (context, feedback) => socket.send({
  // $B1 is the encoder layout: title, value and indicator are its named fields.
  event: "setFeedback", context, payload: feedback
});
const action = new MasterVolumeAction(audio, displayFeedback);
const outputSettings = new Map();
const outputSelectorSettings = new Map();
const appVolumeSettings = new Map();
const visibleOutputContexts = new Set();
const microphoneAction = new MasterVolumeAction({
  get: () => audio.getMicrophone(),
  adjust: (ticks) => audio.adjustMicrophone(ticks),
  toggleMute: () => audio.toggleMicrophoneMute()
}, displayFeedback, "Microphone", "Mic muted");

function updatePlayPauseIcon(context, playback) {
  socket.send({ event: "setState", context, payload: { state: playback.isPlaying ? 1 : 0 } });
}

function updateOutputSettings(context, settings) {
  outputSettings.set(context, {
    outputA: settings?.outputA ?? "",
    outputB: settings?.outputB ?? ""
  });
}

function updateOutputSelectorSettings(context, settings) {
  outputSelectorSettings.set(context, { aliases: settings?.aliases && typeof settings.aliases === "object" ? settings.aliases : {} });
}

function updateAppVolumeSettings(context, settings) {
  appVolumeSettings.set(context, { pid: Number(settings?.pid) || 0, name: settings?.name || "" });
}

async function updateOutputIcon(context) {
  const settings = outputSettings.get(context);
  if (!settings?.outputA || !settings.outputB) return;
  const current = await audio.getDefaultOutput();
  socket.send({ event: "setState", context, payload: { state: current.id === settings.outputB ? 1 : 0 } });
}

async function switchOutput(context) {
  const settings = outputSettings.get(context);
  if (!settings?.outputA || !settings.outputB) {
    throw new Error("Configure both audio outputs in the action settings first.");
  }
  const current = await audio.getDefaultOutput();
  const next = current.id === settings.outputA ? settings.outputB : settings.outputA;
  await audio.setOutput(next);
  socket.send({ event: "setState", context, payload: { state: next === settings.outputB ? 1 : 0 } });
}

function displaySelectedOutput(context, output) {
  const alias = outputSelectorSettings.get(context)?.aliases?.[output.id]?.trim();
  socket.send({
    event: "setFeedback",
    context,
    payload: {
      title: "Output",
      value: alias || output.name || "Unknown output",
    }
  });
}

function displayAppVolume(context, state) {
  const configuredName = appVolumeSettings.get(context)?.name;
  socket.send({
    event: "setFeedback",
    context,
    payload: {
      title: configuredName || state.name,
      value: state.muted ? "Muted" : `${state.level}%`,
      indicator: state.muted ? 0 : state.level
    }
  });
}

async function refreshAppVolume(context) {
  const settings = appVolumeSettings.get(context);
  if (!settings?.pid) {
    socket.send({ event: "setFeedback", context, payload: { title: "App Volume", value: "Configure", indicator: 0 } });
    return;
  }
  displayAppVolume(context, await audio.getAppVolume(settings.pid));
}

setInterval(() => {
  for (const context of visibleOutputContexts) {
    updateOutputIcon(context).catch((error) => console.error("Could not refresh audio-output icon:", error.message));
  }
}, 1500).unref();

socket.connect(async (event) => {
  try {
    if (event.action === OUTPUT_SWITCH_ACTION_UUID && event.event === "didReceiveSettings") {
      updateOutputSettings(event.context, event.payload?.settings);
      await updateOutputIcon(event.context);
      return;
    }
    if (event.action === OUTPUT_SELECTOR_ACTION_UUID && event.event === "didReceiveSettings") {
      updateOutputSelectorSettings(event.context, event.payload?.settings);
      displaySelectedOutput(event.context, await audio.getDefaultOutput());
      return;
    }
    if (event.action === APP_VOLUME_ACTION_UUID && event.event === "didReceiveSettings") {
      updateAppVolumeSettings(event.context, event.payload?.settings);
      await refreshAppVolume(event.context);
      return;
    }
    if ([OUTPUT_SWITCH_ACTION_UUID, OUTPUT_SELECTOR_ACTION_UUID].includes(event.action) && event.event === "sendToPlugin" && event.payload?.event === "getOutputs") {
      socket.send({ event: "sendToPropertyInspector", context: event.context, payload: { event: "outputs", outputs: await audio.listOutputs() } });
      return;
    }
    if (event.action === APP_VOLUME_ACTION_UUID && event.event === "sendToPlugin" && event.payload?.event === "getApps") {
      socket.send({ event: "sendToPropertyInspector", context: event.context, payload: { event: "apps", apps: await audio.listApps() } });
      return;
    }
    if (event.action === MASTER_VOLUME_ACTION_UUID) {
      if (event.event === "willAppear") await action.appear(event.context);
      if (event.event === "dialRotate") await action.rotate(event.context, event.payload?.ticks);
      if (event.event === "dialDown" || event.event === "keyDown") await action.press(event.context);
      return;
    }
    if (event.action === MICROPHONE_VOLUME_ACTION_UUID) {
      if (event.event === "willAppear") await microphoneAction.appear(event.context);
      if (event.event === "dialRotate") await microphoneAction.rotate(event.context, event.payload?.ticks);
      if (event.event === "dialDown" || event.event === "keyDown") await microphoneAction.press(event.context);
      return;
    }
    if (event.action === OUTPUT_SWITCH_ACTION_UUID) {
      if (event.event === "willAppear") {
        visibleOutputContexts.add(event.context);
        updateOutputSettings(event.context, event.payload?.settings);
        await updateOutputIcon(event.context);
      }
      if (event.event === "willDisappear") visibleOutputContexts.delete(event.context);
      if (event.event === "keyDown") await switchOutput(event.context);
      return;
    }
    if (event.action === OUTPUT_SELECTOR_ACTION_UUID) {
      if (event.event === "willAppear") updateOutputSelectorSettings(event.context, event.payload?.settings);
      if (event.event === "willAppear" || event.event === "dialDown" || event.event === "keyDown") {
        displaySelectedOutput(event.context, await audio.getDefaultOutput());
      }
      if (event.event === "dialRotate") {
        const ticks = Number(event.payload?.ticks) || 0;
        if (ticks !== 0) displaySelectedOutput(event.context, await audio.cycleOutput(ticks));
      }
      return;
    }
    if (event.action === APP_VOLUME_ACTION_UUID) {
      if (event.event === "willAppear") {
        updateAppVolumeSettings(event.context, event.payload?.settings);
        await refreshAppVolume(event.context);
      }
      const settings = appVolumeSettings.get(event.context);
      if (event.event === "dialRotate" && settings?.pid) {
        const ticks = Number(event.payload?.ticks) || 0;
        if (ticks !== 0) displayAppVolume(event.context, await audio.adjustAppVolume(settings.pid, ticks));
      }
      if ((event.event === "dialDown" || event.event === "keyDown") && settings?.pid) {
        displayAppVolume(event.context, await audio.toggleAppMute(settings.pid));
      }
      return;
    }
    if (event.action === PLAY_PAUSE_ACTION_UUID) {
      if (event.event === "willAppear") updatePlayPauseIcon(event.context, await audio.getPlaybackState());
      if (event.event === "keyDown") updatePlayPauseIcon(event.context, await audio.togglePlayback());
      return;
    }
    const mediaKey = MEDIA_ACTIONS.get(event.action);
    if (mediaKey && event.event === "keyDown") await audio.sendMediaKey(mediaKey);

    const systemAction = SYSTEM_ACTIONS.get(event.action);
    if (systemAction && event.event === "keyDown") {
      if (systemAction === "lock-pc") {
        await audio.lockWorkstation();
      } else if (systemAction === "shutdown-pc") {
        const expires = shutdownConfirmations.get(event.context) || 0;
        if (expires < Date.now()) {
          shutdownConfirmations.set(event.context, Date.now() + 3000);
          socket.send({ event: "setTitle", context: event.context, payload: { title: "Press again" } });
          setTimeout(() => shutdownConfirmations.delete(event.context), 3000).unref();
        } else {
          shutdownConfirmations.delete(event.context);
          await audio.power(systemAction);
        }
      } else {
        await audio.power(systemAction);
      }
    }
  } catch (error) {
    console.error("Could not run Windows Essentials action:", error.message);
  }
});
