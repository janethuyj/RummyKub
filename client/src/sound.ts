// Tiny sound effects generated with the Web Audio API — no audio files to ship.
// Browsers block audio until a user gesture, so call unlockAudio() on first click.
let ctx: AudioContext | null = null;

function audio(): AudioContext | null {
  if (ctx) return ctx;
  const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!Ctor) return null;
  ctx = new Ctor();
  return ctx;
}

export function unlockAudio(): void {
  const c = audio();
  if (c && c.state === 'suspended') void c.resume();
}

function beep(freq: number, durationMs: number, type: OscillatorType, gain = 0.14): void {
  const c = audio();
  if (!c || c.state !== 'running') return;
  const osc = c.createOscillator();
  const vol = c.createGain();
  osc.type = type;
  osc.frequency.value = freq;
  osc.connect(vol);
  vol.connect(c.destination);
  const now = c.currentTime;
  vol.gain.setValueAtTime(gain, now);
  vol.gain.exponentialRampToValueAtTime(0.0001, now + durationMs / 1000);
  osc.start(now);
  osc.stop(now + durationMs / 1000);
}

/** A bright two-note chime when tiles are played to the board. */
export function playTilePlayed(): void {
  beep(587, 90, 'triangle');
  setTimeout(() => beep(880, 120, 'triangle'), 75);
}

/** A soft low tap when a tile is drawn. */
export function playTileDrawn(): void {
  beep(300, 100, 'sine', 0.12);
}
