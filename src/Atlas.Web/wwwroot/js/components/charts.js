/**
 * Canvas time-series charts (area / line / bars) with explicit gap handling.
 *
 * Two rules from the platform's evidence contract are encoded here:
 *  1. A null value is drawn as a GAP, never interpolated across. A chart that
 *     draws a straight line through missing telemetry is lying.
 *  2. Buckets with no observations are labelled "no data" in the tooltip.
 */

const SERIES_COLOURS = { requests: '#6fb0da', errors: '#e26a6a', latency: '#e6b45f', neutral: '#9db0ba', ok: '#55c98d' };

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

function niceMax(value) {
  if (!Number.isFinite(value) || value <= 0) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const normalized = value / magnitude;
  const step = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
  return step * magnitude;
}

function formatNumber(value) {
  if (!Number.isFinite(value)) return '—';
  const abs = Math.abs(value);
  if (abs >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`;
  if (abs >= 1_000) return `${(value / 1000).toFixed(1)}K`;
  if (abs >= 100) return value.toFixed(0);
  if (abs >= 10) return value.toFixed(1);
  return value.toFixed(2).replace(/\.00$/, '');
}

function formatClock(iso) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

/**
 * @param {HTMLElement} container element carrying [data-atlas-chart]
 * @param {{ labels: string[], series: { name: string, values: (number|null)[], tone?: string, kind?: string, unit?: string }[] }} model
 */
export function renderChart(container, model) {
  const canvas = container.querySelector('canvas');
  if (!canvas || !model?.series?.length) return;

  const { ctx, width, height } = fitCanvas(canvas);
  ctx.clearRect(0, 0, width, height);

  const padding = { left: 46, right: 14, top: 12, bottom: 24 };
  const plotWidth = Math.max(10, width - padding.left - padding.right);
  const plotHeight = Math.max(10, height - padding.top - padding.bottom);

  const allValues = model.series.flatMap((series) => series.values ?? []).filter((value) => Number.isFinite(value));
  const maxValue = allValues.length ? niceMax(Math.max(...allValues) * 1.15) : 1;
  const labels = model.labels ?? [];

  const xFor = (index) => padding.left + (labels.length <= 1 ? plotWidth / 2 : (plotWidth * index) / (labels.length - 1));
  const yFor = (value) => padding.top + plotHeight - (plotHeight * Math.min(value, maxValue)) / maxValue;

  // Grid + y axis
  ctx.font = '10px ui-monospace, monospace';
  ctx.textBaseline = 'middle';
  for (let i = 0; i <= 4; i += 1) {
    const value = (maxValue * i) / 4;
    const y = yFor(value);
    ctx.beginPath();
    ctx.moveTo(padding.left, y);
    ctx.lineTo(width - padding.right, y);
    ctx.strokeStyle = 'rgba(255,255,255,0.06)';
    ctx.lineWidth = 1;
    ctx.stroke();
    ctx.fillStyle = 'rgba(157,176,186,0.75)';
    ctx.textAlign = 'right';
    ctx.fillText(formatNumber(value), padding.left - 8, y);
  }

  // x axis labels
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  const labelStep = Math.max(1, Math.ceil(labels.length / 6));
  labels.forEach((label, index) => {
    if (index % labelStep !== 0 && index !== labels.length - 1) return;
    ctx.fillStyle = 'rgba(157,176,186,0.7)';
    ctx.fillText(formatClock(label), xFor(index), height - padding.bottom + 6);
  });

  model.series.forEach((series) => {
    const colour = SERIES_COLOURS[series.tone ?? series.name] ?? SERIES_COLOURS.neutral;
    const values = series.values ?? [];
    const kind = series.kind ?? 'area';

    if (kind === 'bars') {
      const barWidth = Math.max(2, plotWidth / Math.max(1, values.length) - 2);
      values.forEach((value, index) => {
        if (!Number.isFinite(value)) return; // gap: no bar is drawn for missing data
        const x = xFor(index) - barWidth / 2;
        const y = yFor(value);
        ctx.fillStyle = colour;
        ctx.globalAlpha = 0.75;
        ctx.fillRect(x, y, barWidth, padding.top + plotHeight - y);
        ctx.globalAlpha = 1;
      });
      return;
    }

    // Area/line with gap segmentation.
    ctx.lineWidth = 1.8;
    ctx.strokeStyle = colour;
    ctx.beginPath();
    let drawing = false;
    values.forEach((value, index) => {
      if (!Number.isFinite(value)) {
        drawing = false;
        return;
      }
      const x = xFor(index);
      const y = yFor(value);
      if (!drawing) {
        ctx.moveTo(x, y);
        drawing = true;
      } else {
        ctx.lineTo(x, y);
      }
    });
    ctx.stroke();

    if (kind === 'area') {
      const gradient = ctx.createLinearGradient(0, padding.top, 0, padding.top + plotHeight);
      gradient.addColorStop(0, `${colour}44`);
      gradient.addColorStop(1, `${colour}05`);
      ctx.fillStyle = gradient;

      let segment = [];
      const flush = () => {
        if (segment.length < 2) {
          segment = [];
          return;
        }
        ctx.beginPath();
        segment.forEach(([x, y], index) => (index === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y)));
        ctx.lineTo(segment[segment.length - 1][0], padding.top + plotHeight);
        ctx.lineTo(segment[0][0], padding.top + plotHeight);
        ctx.closePath();
        ctx.fill();
        segment = [];
      };

      values.forEach((value, index) => {
        if (!Number.isFinite(value)) {
          flush();
          return;
        }
        segment.push([xFor(index), yFor(value)]);
      });
      flush();
    }
  });

  container._atlasChart = { model, padding, plotWidth, plotHeight, maxValue, xFor, yFor, width, height };
  attachHover(container, canvas);
}

function attachHover(container, canvas) {
  if (container.dataset.hoverBound === 'true') return;
  container.dataset.hoverBound = 'true';

  const tooltip = container.querySelector('.atlas-chart__tooltip');
  const readout = container.querySelector('.atlas-chart__readout');

  canvas.addEventListener('mousemove', (event) => {
    const state = container._atlasChart;
    if (!state || !tooltip) return;

    const rect = canvas.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const count = state.model.labels.length;
    if (count === 0) return;

    const ratio = (x - state.padding.left) / state.plotWidth;
    const index = Math.max(0, Math.min(count - 1, Math.round(ratio * (count - 1))));
    const label = state.model.labels[index];

    const lines = state.model.series.map((series) => {
      const value = series.values?.[index];
      const text = Number.isFinite(value) ? `${formatNumber(value)}${series.unit ?? ''}` : 'no data';
      return `${series.name}: ${text}`;
    });

    tooltip.replaceChildren();
    const head = document.createElement('div');
    head.textContent = new Date(label).toLocaleString();
    tooltip.appendChild(head);
    lines.forEach((line) => {
      const row = document.createElement('div');
      row.textContent = line;
      tooltip.appendChild(row);
    });

    tooltip.classList.add('is-visible');
    const left = Math.min(Math.max(6, x - 60), rect.width - 150);
    tooltip.style.left = `${left}px`;
    tooltip.style.top = '8px';

    if (readout) readout.textContent = `${new Date(label).toLocaleTimeString()} · ${lines.join(' · ')}`;
  });

  canvas.addEventListener('mouseleave', () => {
    tooltip?.classList.remove('is-visible');
  });
}

/** Sparkline: same data contract, minimal chrome. */
export function renderSparkline(canvas, values, tone = 'neutral') {
  const { ctx, width, height } = fitCanvas(canvas);
  ctx.clearRect(0, 0, width, height);
  const finite = values.filter((value) => Number.isFinite(value));
  if (finite.length === 0) return;

  const min = Math.min(...finite);
  const max = Math.max(...finite);
  const span = max - min || 1;
  const colour = SERIES_COLOURS[tone] ?? SERIES_COLOURS.neutral;

  ctx.beginPath();
  let drawing = false;
  values.forEach((value, index) => {
    if (!Number.isFinite(value)) {
      drawing = false;
      return;
    }
    const x = (width * index) / Math.max(1, values.length - 1);
    const y = height - 4 - ((height - 8) * (value - min)) / span;
    if (!drawing) {
      ctx.moveTo(x, y);
      drawing = true;
    } else {
      ctx.lineTo(x, y);
    }
  });
  ctx.strokeStyle = colour;
  ctx.lineWidth = 1.4;
  ctx.stroke();
}

/** Initialises every chart on the page from its JSON island. */
export function initCharts(root = document, sourceReader = null) {
  root.querySelectorAll('[data-atlas-chart]').forEach((container) => {
    const sourceId = container.dataset.chartSource;
    const model = sourceReader ? sourceReader(sourceId) : null;
    if (model) renderChart(container, model);
  });
}
