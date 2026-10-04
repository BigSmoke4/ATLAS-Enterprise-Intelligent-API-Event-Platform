/**
 * Confirmation dialog for consequential operations.
 *
 * Talks only about what will happen and what the server will record; it does
 * not editorialise. Keyboard: Escape cancels, focus is trapped while open and
 * restored to the invoking element afterwards.
 */

let activeDialog = null;

export function confirmAction({ title = 'Confirm action', message = '', hint = '', confirmLabel = 'CONFIRM', cancelLabel = 'CANCEL', tone = 'danger' } = {}) {
  return new Promise((resolve) => {
    const previousFocus = document.activeElement;

    const overlay = document.createElement('div');
    overlay.className = 'atlas-modal-overlay';
    overlay.dataset.atlasModal = '';

    const dialog = document.createElement('div');
    dialog.className = 'atlas-modal';
    dialog.setAttribute('role', 'dialog');
    dialog.setAttribute('aria-modal', 'true');
    dialog.setAttribute('aria-label', title);

    const heading = document.createElement('h2');
    heading.className = 'atlas-modal__title';
    heading.textContent = title;

    const body = document.createElement('p');
    body.className = 'atlas-modal__message';
    body.textContent = message;

    dialog.append(heading, body);

    if (hint) {
      const note = document.createElement('p');
      note.className = 'atlas-modal__hint';
      note.textContent = hint;
      dialog.appendChild(note);
    }

    const actions = document.createElement('div');
    actions.className = 'atlas-modal__actions';

    const cancel = document.createElement('button');
    cancel.type = 'button';
    cancel.className = 'atlas-button atlas-button--ghost';
    cancel.textContent = cancelLabel;

    const confirm = document.createElement('button');
    confirm.type = 'button';
    confirm.className = `atlas-button ${tone === 'danger' ? 'atlas-button--danger' : 'atlas-button--primary'}`;
    confirm.textContent = confirmLabel;

    actions.append(cancel, confirm);
    dialog.appendChild(actions);
    overlay.appendChild(dialog);
    document.body.appendChild(overlay);
    activeDialog = overlay;

    const close = (result) => {
      document.removeEventListener('keydown', onKeyDown, true);
      overlay.remove();
      activeDialog = null;
      if (previousFocus instanceof HTMLElement) previousFocus.focus();
      resolve(result);
    };

    const onKeyDown = (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        close(false);
        return;
      }
      if (event.key !== 'Tab') return;

      const focusable = dialog.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    cancel.addEventListener('click', () => close(false));
    confirm.addEventListener('click', () => close(true));
    overlay.addEventListener('mousedown', (event) => {
      if (event.target === overlay) close(false);
    });
    document.addEventListener('keydown', onKeyDown, true);

    confirm.focus();
  });
}

export function isDialogOpen() {
  return activeDialog !== null;
}
