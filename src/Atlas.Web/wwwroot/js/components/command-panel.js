/**
 * Command bar: refresh cadence, manual refresh and live-connection indicator.
 *
 * Page modules register refresh handlers; the bar is the single place that
 * decides how often they run. Handlers are always asynchronous and always
 * fetch from the server, so what the operator sees is a real reading with a
 * visible timestamp — not an interpolation.
 */

const handlers = new Map();
let cadenceMs = 30000;
let timer = null;
let running = false;

export function registerRefreshHandler(name, handler) {
  handlers.set(name, handler);
  return () => handlers.delete(name);
}

export async function refreshAll({ silent = false } = {}) {
  if (running) return;
  running = true;

  const status = document.querySelector('[data-atlas-refresh-status]');
  try {
    await Promise.all([...handlers.values()].map(async (handler) => {
      try {
        await handler();
      } catch (error) {
        if (!silent) {
          const { notifyWarn } = await import('./../core/notifications.js');
          notifyWarn('Refresh failed', error?.detail ?? error?.message ?? 'Unknown error');
        }
      }
    }));

    document.querySelectorAll('[data-atlas-updated]').forEach((element) => {
      element.textContent = new Date().toLocaleTimeString();
    });
    if (status) status.textContent = `Updated ${new Date().toLocaleTimeString()}`;
  } finally {
    running = false;
  }
}

function schedule() {
  if (timer) window.clearInterval(timer);
  if (cadenceMs > 0) timer = window.setInterval(() => refreshAll({ silent: true }), cadenceMs);
}

export function initCommandPanel(root = document) {
  const panel = root.querySelector('[data-atlas-command]');
  if (!panel) return;

  const cadenceSelect = panel.querySelector('[data-atlas-cadence]');
  cadenceMs = Number(cadenceSelect?.value ?? panel.dataset.cadence ?? 30000);
  schedule();

  cadenceSelect?.addEventListener('change', () => {
    cadenceMs = Number(cadenceSelect.value);
    schedule();
  });

  panel.querySelectorAll('[data-atlas-refresh]').forEach((button) => {
    button.addEventListener('click', async () => {
      button.disabled = true;
      try {
        await refreshAll();
      } finally {
        button.disabled = false;
      }
    });
  });

  document.querySelectorAll('[data-atlas-updated]').forEach((element) => {
    if (!element.textContent?.trim()) element.textContent = new Date().toLocaleTimeString();
  });
}
