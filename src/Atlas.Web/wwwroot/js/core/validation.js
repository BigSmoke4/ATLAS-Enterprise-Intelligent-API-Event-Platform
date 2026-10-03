export function installKeyboardFocus() {
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape') document.querySelector('[data-modal][open]')?.close();
  });
}

export function required(value, fieldName) {
  if (!String(value ?? '').trim()) throw new Error(`${fieldName} is required.`);
  return value;
}
