export function notify(message, kind = 'info') {
  const node = document.createElement('div'); node.className = `atlas-alert atlas-alert--${kind}`; node.role = 'status'; node.textContent = message;
  document.body.prepend(node); setTimeout(() => node.remove(), 5000); return node;
}
