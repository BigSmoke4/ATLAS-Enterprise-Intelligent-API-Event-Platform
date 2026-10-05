/**
 * Client-side form validation.
 *
 * The browser's constraint validation is the source of truth here (the server
 * validates everything again — this only avoids a pointless round trip), and
 * messages are written next to the field that failed rather than in a generic
 * summary the operator has to map back to an input.
 */

function fieldContainer(input) {
  return input.closest('.atlas-field') ?? input.parentElement;
}

function clearError(input) {
  const container = fieldContainer(input);
  container?.querySelector('.atlas-field__error')?.remove();
  input.removeAttribute('aria-invalid');
}

function showError(input, message) {
  const container = fieldContainer(input);
  if (!container) return;
  clearError(input);
  const error = document.createElement('span');
  error.className = 'atlas-field__error';
  error.textContent = message;
  container.appendChild(error);
  input.setAttribute('aria-invalid', 'true');
}

/** Returns the serialisable payload of a form as a plain object. */
export function serializeForm(form) {
  const payload = {};
  (form.elements ? Array.from(form.elements) : []).forEach((element) => {
    if (!element.name || element.disabled) return;
    if (element.type === 'checkbox') {
      payload[element.name] = element.checked;
    } else if (element.type === 'number' || element.type === 'range') {
      payload[element.name] = element.value === '' ? null : Number(element.value);
    } else {
      payload[element.name] = element.value;
    }
  });
  return payload;
}

export function validateForm(form) {
  let valid = true;
  const invalid = [];

  form.querySelectorAll('input, select, textarea').forEach((input) => {
    clearError(input);
    if (!(input instanceof HTMLInputElement || input instanceof HTMLSelectElement || input instanceof HTMLTextAreaElement)) return;
    if (input.disabled || input.type === 'hidden' || input.type === 'button' || input.type === 'submit') return;
    if (!input.checkValidity()) {
      valid = false;
      invalid.push(input);
      showError(input, input.validationMessage || 'This value is not valid.');
    }
  });

  if (invalid.length > 0) invalid[0].focus();
  return valid;
}

/**
 * Wires a form to a submit handler: validates first, then runs the handler,
 * and reports whatever the server said if the request fails.
 */
export function bindForm(form, { onSubmit, onError, resetOnSuccess = false } = {}) {
  if (!form) return () => {};

  const submit = async (event) => {
    event.preventDefault();
    if (!validateForm(form)) return;

    const buttons = form.querySelectorAll('button[type="submit"], button:not([type])');
    buttons.forEach((button) => { button.disabled = true; });

    try {
      await onSubmit?.(serializeForm(form), form);
      if (resetOnSuccess) form.reset();
    } catch (error) {
      if (onError) onError(error);
      else showError(form.querySelector('input, select, textarea') ?? form, error?.detail ?? error?.message ?? 'Request failed.');
    } finally {
      buttons.forEach((button) => { button.disabled = false; });
    }
  };

  form.addEventListener('submit', submit);
  return () => form.removeEventListener('submit', submit);
}
