/**
 * Instrument renderers: radial gauges, LED meters, bars and rings.
 *
 * All of them read their value from a data attribute that Razor rendered from
 * real data. If the attribute is missing (no telemetry), the instrument draws
 * an empty/idle state and leaves its label as "no data" — it never animates to
 * a plausible-looking number.
 */

const COLOURS = {
  ok: '#55c98d',
  warn: '#e6b45f',
  bad: '#e26a6a',
  idle: '#5b6c76',
  info: '#6fb0da'
};

function toneFor(fraction, warnAt, badAt) {
  if (fraction === null) return COLOURS.idle;
  if (badAt !== null && fraction >= badAt) return COLOURS.bad;
  if (warnAt !== null && fraction >= warnAt) return COLOURS.warn;
  return COLOURS.ok;
}

function readNumber(element, name, fallback = null) {
  const raw = element.dataset[name];
  if (raw === undefined || raw === '') return fallback;
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function fitCanvas(canvas) {
  const ratio = window.devicePixelRatio || 1;
  const rect = canvas.getBoundingClientRect();
  const width = Math.max(1, Math.round(rect.width * ratio));
  const height = Math.max(1, Math.round(rect.height * ratio));
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  const ctx = canvas.getContext('2d');
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  return { ctx, width: rect.width, height: rect.height };
}

export function renderGauge(canvas, { value, min = 0, max = 100, warnAt = null, badAt = null, label = '', unit = '' } = {}) {
  const { ctx, width, height } = fitCanvas(canvas);
  ctx.clearRect(0, 0, width, height);

  const radius = Math.min(width, height) / 2 - 8;
  const cx = width / 2;
  const cy = height / 2;
  const start = Math.PI * 0.75;
  const sweep = Math.PI * 1.5;

  const hasValue = Number.isFinite(value) && max > min;
  const fraction = hasValue ? Math.min(1, Math.max(0, (value - min) / (max - min))) : null;

  // Track
  ctx.beginPath();
  ctx.arc(cx, cy, radius, start, start + sweep);
  ctx.strokeStyle = 'rgba(255,255,255,0.07)';
  ctx.lineWidth = 9;
  ctx.lineCap = 'round';
  ctx.stroke();

  // Ticks
  ctx.strokeStyle = 'rgba(255,255,255,0.12)';
  ctx.lineWidth = 1;
  for (let i = 0; i <= 10; i += 1) {
    const angle = start + (sweep * i) / 10;
    const inner = radius - 6;
    const outer = radius + 2;
    ctx.beginPath();
    ctx.moveTo(cx + Math.cos(angle) * inner, cy + Math.sin(angle) * inner);
    ctx.lineTo(cx + Math.cos(angle) * outer, cy + Math.sin(angle) * outer);
    ctx.stroke();
  }

  if (fraction !== null) {
    const colour = toneFor(fraction, warnAt, badAt);
    ctx.beginPath();
    ctx.arc(cx, cy, radius, start, start + sweep * fraction);
    ctx.strokeStyle = colour;
    ctx.lineWidth = 7;
    ctx.lineCap = 'round';
    ctx.shadowColor = colour;
    ctx.shadowBlur = 8;
    ctx.stroke();
    ctx.shadowBlur = 0;

    const needle = start + sweep * fraction;
    ctx.beginPath();
    ctx.moveTo(cx, cy);
    ctx.lineTo(cx + Math.cos(needle) * (radius - 12), cy + Math.sin(needle) * (radius - 12));
    ctx.strokeStyle = 'rgba(226,236,241,0.75)';
    ctx.lineWidth = 1.4;
    ctx.stroke();
  }

  ctx.beginPath();
  ctx.arc(cx, cy, 3.2, 0, Math.PI * 2);
  ctx.fillStyle = fraction === null ? COLOURS.idle : 'rgba(226,236,241,0.9)';
  ctx.fill();

  const readout = canvas.parentElement?.querySelector('.atlas-gauge__value');
  if (readout) {
    readout.textContent = fraction === null ? '—' : `${formatCompact(value)}${unit}`;
    readout.title = fraction === null ? 'No telemetry available.' : `${label} ${value}${unit}`.trim();
  }
}

export function renderMeter(element, { fraction, warnAt = 0.75, badAt = 0.9 } = {}) {
  const track = element.querySelector('.atlas-meter__track');
  if (!track) return;

  const segments = Number(element.dataset.segments ?? 18);
  if (track.children.length !== segments) {
    track.replaceChildren();
    for (let i = 0; i < segments; i += 1) {
      const segment = document.createElement('span');
      segment.className = 'atlas-meter__segment';
      track.appendChild(segment);
    }
  }

  const lit = fraction === null ? 0 : Math.round(Math.min(1, Math.max(0, fraction)) * segments);
  const tone = fraction === null ? null : fraction >= badAt ? 'is-bad' : fraction >= warnAt ? 'is-warn' : '';

  Array.from(track.children).forEach((segment, index) => {
    const isLit = index < lit;
    segment.classList.toggle('is-lit', isLit);
    segment.classList.toggle('is-warn', isLit && tone === 'is-warn');
    segment.classList.toggle('is-bad', isLit && tone === 'is-bad');
  });

  const readout = element.querySelector('.atlas-meter__value');
  if (readout) readout.textContent = fraction === null ? 'no data' : `${Math.round(fraction * 100)}%`;
}

export function renderBar(element, fraction, tone = 'info') {
  const fill = element.querySelector('.atlas-bar__fill');
  if (!fill) return;
  fill.classList.remove('atlas-bar__fill--warn', 'atlas-bar__fill--bad', 'atlas-bar__fill--info');
  if (tone === 'warn') fill.classList.add('atlas-bar__fill--warn');
  else if (tone === 'bad') fill.classList.add('atlas-bar__fill--bad');
  else if (tone === 'info') fill.classList.add('atlas-bar__fill--info');
  fill.style.width = fraction === null ? '0%' : `${Math.round(Math.min(1, Math.max(0, fraction)) * 100)}%`;
}

export function renderRing(element, fraction) {
  if (fraction === null) {
    element.style.setProperty('--atlas-value', '0');
    element.style.setProperty('--atlas-ring-colour', COLOURS.idle);
    return;
  }
  const clamped = Math.min(1, Math.max(0, fraction));
  element.style.setProperty('--atlas-value', String(clamped));
  element.style.setProperty('--atlas-ring-colour', toneFor(clamped, 0.75, 0.9));
}

export function formatCompact(value) {
  if (!Number.isFinite(value)) return '—';
  const abs = Math.abs(value);
  if (abs >= 1_000_000_000) return `${(value / 1_000_000_000).toFixed(1)}B`;
  if (abs >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`;
  if (abs >= 10_000) return `${(value / 1000).toFixed(1)}K`;
  if (abs >= 100) return Math.round(value).toString();
  if (abs >= 1) return value.toFixed(1);
  if (abs === 0) return '0';
  return value.toFixed(3);
}

/** Scans a subtree for instruments and renders each from its data attributes. */
export function initInstruments(root = document) {
  root.querySelectorAll('[data-atlas-gauge]').forEach((element) => {
    const canvas = element.querySelector('canvas');
    if (!canvas) return;
    renderGauge(canvas, {
      value: readNumber(element, 'value'),
      min: readNumber(element, 'min', 0) ?? 0,
      max: readNumber(element, 'max', 100) ?? 100,
      warnAt: readNumber(element, 'warnAt'),
      badAt: readNumber(element, 'badAt'),
      label: element.dataset.label ?? '',
      unit: element.dataset.unit ?? ''
    });
  });

  root.querySelectorAll('[data-atlas-meter]').forEach((element) => {
    renderMeter(element, {
      fraction: readNumber(element, 'value'),
      warnAt: readNumber(element, 'warnAt', 0.75) ?? 0.75,
      badAt: readNumber(element, 'badAt', 0.9) ?? 0.9
    });
  });

  root.querySelectorAll('[data-atlas-bar]').forEach((element) => {
    renderBar(element, readNumber(element, 'value'), element.dataset.tone ?? 'info');
  });

  root.querySelectorAll('[data-atlas-ring]').forEach((element) => {
    renderRing(element, readNumber(element, 'value'));
  });
}

/** Re-renders instruments when their container resizes (dial canvases are fixed-size). */
export function observeInstruments(root = document) {
  if (typeof ResizeObserver === 'undefined') return;
  const observer = new ResizeObserver((entries) => {
    entries.forEach((entry) => {
      if (entry.target.matches('[data-atlas-gauge], [data-atlas-chart]')) initInstruments(entry.target.parentElement ?? document);
    });
  });
  root.querySelectorAll('[data-atlas-gauge]').forEach((element) => observer.observe(element));
}
