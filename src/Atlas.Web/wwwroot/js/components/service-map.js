/**
 * Service topology map.
 *
 * The graph is generated from real registry data (services + their declared
 * dependencies + aggregate instance health) rendered by Razor into a JSON
 * island. Layout is deterministic (longest-path layering), so the same
 * registry state always produces the same picture — an operations map that
 * reshuffles itself on every refresh is useless.
 *
 * Interactions: pan, zoom, search/highlight, click-to-select (the page module
 * fills the detail panel from the same payload).
 */

const SVG_NS = 'http://www.w3.org/2000/svg';
const COLUMN_WIDTH = 210;
const ROW_HEIGHT = 88;
const NODE_WIDTH = 168;
const NODE_HEIGHT = 52;

function el(name, attributes = {}) {
  const element = document.createElementNS(SVG_NS, name);
  Object.entries(attributes).forEach(([key, value]) => element.setAttribute(key, String(value)));
  return element;
}

function healthTone(health) {
  switch ((health ?? '').toLowerCase()) {
    case 'healthy': return 'ok';
    case 'degraded': return 'warn';
    case 'unhealthy':
    case 'unavailable': return 'bad';
    default: return 'idle';
  }
}

function computeLevels(nodes) {
  const byId = new Map(nodes.map((node) => [node.id, node]));
  const levels = new Map();
  const visiting = new Set();

  function levelOf(id) {
    if (levels.has(id)) return levels.get(id);
    if (visiting.has(id)) return 0; // cycle guard: registry data is validated, but never hang the UI
    visiting.add(id);

    const node = byId.get(id);
    const dependencies = node?.dependsOn ?? [];
    const level = dependencies.length === 0
      ? 0
      : 1 + Math.max(...dependencies.map((dependencyId) => (byId.has(dependencyId) ? levelOf(dependencyId) : 0)));

    visiting.delete(id);
    levels.set(id, level);
    return level;
  }

  nodes.forEach((node) => levelOf(node.id));
  return levels;
}

export function renderTopology(container, model, { onSelect = null, searchInput = null } = {}) {
  const nodes = model?.nodes ?? [];
  if (nodes.length === 0) {
    container.replaceChildren();
    const empty = document.createElement('div');
    empty.className = 'atlas-empty';
    empty.innerHTML = '<span class="atlas-empty__glyph">◌</span><span class="atlas-empty__title">No telemetry available.</span><span class="atlas-empty__hint">No services are registered for this organization yet.</span>';
    container.appendChild(empty);
    return;
  }

  const levels = computeLevels(nodes);
  const columns = new Map();
  nodes.forEach((node) => {
    const level = levels.get(node.id) ?? 0;
    if (!columns.has(level)) columns.set(level, []);
    columns.get(level).push(node);
  });

  const position = new Map();
  [...columns.keys()].sort((a, b) => a - b).forEach((level) => {
    columns.get(level)
      .slice()
      .sort((a, b) => a.name.localeCompare(b.name))
      .forEach((node, index) => {
        position.set(node.id, { x: level * COLUMN_WIDTH + 30, y: index * ROW_HEIGHT + 30 });
      });
  });

  const width = Math.max(container.clientWidth || 800, (columns.size + 1) * COLUMN_WIDTH);
  const height = Math.max(container.clientHeight || 420, Math.max(...[...columns.values()].map((column) => column.length)) * ROW_HEIGHT + 60);

  const svg = el('svg', { viewBox: `0 0 ${width} ${height}`, role: 'img', 'aria-label': 'Service dependency topology' });

  const defs = el('defs');
  const gradient = el('linearGradient', { id: 'atlas-node-fill', x1: '0', y1: '0', x2: '0', y2: '1' });
  gradient.appendChild(el('stop', { offset: '0%', 'stop-color': '#28353e' }));
  gradient.appendChild(el('stop', { offset: '100%', 'stop-color': '#18222a' }));
  defs.appendChild(gradient);

  const marker = el('marker', { id: 'atlas-arrow', viewBox: '0 0 10 10', refX: '9', refY: '5', markerWidth: '6', markerHeight: '6', orient: 'auto-start-reverse' });
  marker.appendChild(el('path', { d: 'M 0 0 L 10 5 L 0 10 z', fill: '#4a6270' }));
  defs.appendChild(marker);
  svg.appendChild(defs);

  const stage = el('g', { class: 'atlas-topology__stage' });
  svg.appendChild(stage);

  const edges = [];
  nodes.forEach((node) => {
    (node.dependsOn ?? []).forEach((dependencyId) => {
      const from = position.get(node.id);
      const to = position.get(dependencyId);
      if (!from || !to) return;
      edges.push({ from, to, tone: healthTone(node.health) });
    });
  });

  edges.forEach((edge) => {
    const startX = edge.from.x + NODE_WIDTH;
    const startY = edge.from.y + NODE_HEIGHT / 2;
    const endX = edge.to.x;
    const endY = edge.to.y + NODE_HEIGHT / 2;
    const midX = (startX + endX) / 2;
    const path = el('path', {
      class: `atlas-topology__edge${edge.tone === 'warn' ? ' atlas-topology__edge--warn' : edge.tone === 'bad' ? ' atlas-topology__edge--bad' : ''}`,
      d: `M ${startX} ${startY} C ${midX} ${startY}, ${midX} ${endY}, ${endX} ${endY}`,
      'marker-end': 'url(#atlas-arrow)'
    });
    stage.appendChild(path);
  });

  const nodeElements = new Map();
  nodes.forEach((node) => {
    const point = position.get(node.id);
    const group = el('g', { class: 'atlas-topology__node', transform: `translate(${point.x},${point.y})`, tabindex: '0', role: 'button' });
    group.dataset.nodeId = node.id;

    group.appendChild(el('rect', { width: NODE_WIDTH, height: NODE_HEIGHT, rx: 6 }));

    const lamp = el('circle', { cx: 14, cy: 16, r: 4, fill: toneColour(healthTone(node.health)) });
    group.appendChild(lamp);

    const title = el('text', { x: 26, y: 20 });
    title.textContent = node.name.length > 20 ? `${node.name.slice(0, 19)}…` : node.name;
    group.appendChild(title);

    const detail = el('text', { x: 14, y: 38, fill: '#9db0ba', 'font-size': '10' });
    detail.textContent = node.instances > 0
      ? `${node.healthyInstances}/${node.instances} instances · ${node.requestsPerSecond.toFixed(1)} rps`
      : 'no instances';
    group.appendChild(detail);

    stage.appendChild(group);
    nodeElements.set(node.id, group);

    const select = () => {
      nodeElements.forEach((element) => element.classList.remove('is-selected'));
      group.classList.add('is-selected');
      onSelect?.(node);
    };

    group.addEventListener('click', select);
    group.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        select();
      }
    });
  });

  container.replaceChildren(svg);
  attachViewport(svg, container, stage);
  attachSearch(searchInput, nodeElements, nodes);
}

function toneColour(tone) {
  switch (tone) {
    case 'ok': return '#55c98d';
    case 'warn': return '#e6b45f';
    case 'bad': return '#e26a6a';
    default: return '#5b6c76';
  }
}

function attachViewport(svg, container, stage) {
  let scale = 1;
  let offsetX = 0;
  let offsetY = 0;
  let dragging = false;
  let lastX = 0;
  let lastY = 0;

  const apply = () => stage.setAttribute('transform', `translate(${offsetX},${offsetY}) scale(${scale})`);
  apply();

  svg.addEventListener('pointerdown', (event) => {
    dragging = true;
    lastX = event.clientX;
    lastY = event.clientY;
    svg.classList.add('is-panning');
    svg.setPointerCapture(event.pointerId);
  });

  svg.addEventListener('pointermove', (event) => {
    if (!dragging) return;
    offsetX += event.clientX - lastX;
    offsetY += event.clientY - lastY;
    lastX = event.clientX;
    lastY = event.clientY;
    apply();
  });

  const stop = (event) => {
    dragging = false;
    svg.classList.remove('is-panning');
    if (event.pointerId !== undefined) svg.releasePointerCapture?.(event.pointerId);
  };

  svg.addEventListener('pointerup', stop);
  svg.addEventListener('pointercancel', stop);

  svg.addEventListener('wheel', (event) => {
    event.preventDefault();
    const next = Math.min(2.4, Math.max(0.35, scale * (event.deltaY > 0 ? 0.92 : 1.08)));
    const rect = svg.getBoundingClientRect();
    const pointerX = event.clientX - rect.left;
    const pointerY = event.clientY - rect.top;
    offsetX = pointerX - ((pointerX - offsetX) * next) / scale;
    offsetY = pointerY - ((pointerY - offsetY) * next) / scale;
    scale = next;
    apply();
  }, { passive: false });

  container.querySelectorAll('[data-topology-zoom]').forEach((button) => {
    button.addEventListener('click', () => {
      const direction = button.dataset.topologyZoom;
      if (direction === 'reset') {
        scale = 1;
        offsetX = 0;
        offsetY = 0;
      } else {
        scale = Math.min(2.4, Math.max(0.35, scale * (direction === 'in' ? 1.15 : 0.87)));
      }
      apply();
    });
  });
}

function attachSearch(input, nodeElements, nodes) {
  if (!input) return;
  input.addEventListener('input', () => {
    const query = input.value.trim().toLowerCase();
    nodeElements.forEach((element, id) => {
      const node = nodes.find((candidate) => candidate.id === id);
      const matches = query === '' || (node?.name ?? '').toLowerCase().includes(query);
      element.style.opacity = matches ? '1' : '0.18';
    });
  });
}
