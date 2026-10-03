export function setGauge(element, value) {
  const bounded = Math.max(0, Math.min(100, Number(value) || 0));
  element?.style.setProperty('--gauge-value', `${bounded}%`);
  if (element) element.querySelector('.atlas-gauge__value')?.style.setProperty('width', `${bounded}%`);
}
