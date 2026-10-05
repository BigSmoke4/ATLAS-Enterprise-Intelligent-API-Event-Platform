/**
 * Toast rail + inline status reporting.
 *
 * Failures are reported with the server's detail text. Nothing is swallowed:
 * if an operator cannot see why an operation failed, it did not "work".
 */

const TONE_CLASS = {
  ok: 'atlas-toast--ok',
  info: 'atlas-toast--info',
  warn: 'atlas-toast--warn',
  error: 'atlas-toast--error'
};

function rail() {
  let element = document.querySelector('[data-atlas-toasts]');
  if (element) return element;

  element = document.createElement('div');
  element.className = 'atlas-toasts';
  element.dataset.atlasToasts = '';
  element.setAttribute('role', 'status');
  element.setAttribute('aria-live', 'polite');
  document.body.appendChild(element);
  return element;
}

export function notify(message, { tone = 'info', detail = '', timeoutMs = 6000 } = {}) {
  if (!message) return;
  const toast = document.createElement('div');
  toast.className = `atlas-toast ${TONE_CLASS[tone] ?? TONE_CLASS.info}`;

  const title = document.createElement('div');
  title.className = 'atlas-toast__title';
  title.textContent = message;
  toast.appendChild(title);

  if (detail) {
    const body = document.createElement('div');
    body.className = 'atlas-toast__detail';
    body.textContent = detail;
    toast.appendChild(body);
  }

  const dismiss = document.createElement('button');
  dismiss.type = 'button';
  dismiss.className = 'atlas-button atlas-button--ghost atlas-button--sm';
  dismiss.textContent = 'DISMISS';
  dismiss.addEventListener('click', () => toast.remove());
  toast.appendChild(dismiss);

  rail().appendChild(toast);
  if (timeoutMs > 0) {
    window.setTimeout(() => toast.remove(), timeoutMs);
  }
  return toast;
}

export const notifyOk = (message, detail) => notify(message, { tone: 'ok', detail });
export const notifyInfo = (message, detail) => notify(message, { tone: 'info', detail });
export const notifyWarn = (message, detail) => notify(message, { tone: 'warn', detail, timeoutMs: 9000 });
export const notifyError = (message, detail) => notify(message, { tone: 'error', detail, timeoutMs: 12000 });

/** Turns an ApiError into an honest message: the server's reason first. */
export function notifyFromError(error, fallback = 'Request failed') {
  const detail = error?.detail ?? error?.problem?.detail ?? '';
  const title = error?.message ?? fallback;
  if (error?.status === 403) {
    return notifyError('Not authorised', detail || 'Your role does not permit this operation.');
  }
  return notifyError(title, detail);
}

/** Writes a short status line into an element (used next to affected forms). */
export function reportStatus(element, message, tone = 'info') {
  if (!element) return;
  element.textContent = message ?? '';
  element.dataset.tone = tone;
  element.classList.remove('atlas-text-dim');
  if (tone === 'error') element.classList.add('atlas-text-danger');
  else element.classList.remove('atlas-text-danger');
}
