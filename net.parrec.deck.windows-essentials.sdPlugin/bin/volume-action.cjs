"use strict";

/** Domain logic kept independent from OpenDeck so it can be tested without hardware. */
class MasterVolumeAction {
  constructor(audio, display, label = "Master volume", mutedLabel = "Muted") {
    this.audio = audio;
    this.display = display;
    this.label = label;
    this.mutedLabel = mutedLabel;
  }

  async appear(context) {
    this.display(context, this.feedback(await this.audio.get()));
  }

  async rotate(context, ticks) {
    const signedTicks = Number(ticks) || 0;
    if (signedTicks === 0) return;
    this.display(context, this.feedback(await this.audio.adjust(signedTicks)));
  }

  async press(context) {
    this.display(context, this.feedback(await this.audio.toggleMute()));
  }

  feedback(state) {
    return {
      title: state.muted ? this.mutedLabel : this.label,
      value: state.muted ? "Muted" : `${state.level}%`,
      indicator: state.muted ? 0 : state.level
    };
  }
}

module.exports = { MasterVolumeAction };
