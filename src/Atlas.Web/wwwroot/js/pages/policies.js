/**
 * Policy engine module: activate/deactivate stored rules and author new
 * data-only rules.
 *
 * A rule is "field OP value" data — the console composes that data and posts
 * it; it never sends or executes an expression string. Activation and
 * deactivation are PlatformAdmin operations and are audit-logged server-side,
 * which is why both pass through an explicit confirmation.
 */

import { get, post, readJsonIsland } from './../core/api-client.js';
import { notifyFromError, notifyOk, reportStatus } from './../core/notifications.js';
import { registerRefreshHandler, refreshAll } from './../components/command-panel.js';
import { confirmAction } from './../core/modal.js';
import { bindForm } from './../core/validation.js';

const bootstrap = readJsonIsland('policies-bootstrap', { organizationId: '', canManage: false });
const organisationId = bootstrap.organizationId || (document.body.dataset.organizationId ?? '');

const OPERATORS = [
  ['GreaterThan', '>'],
  ['GreaterThanOrEqual', '>='],
  ['LessThan', '<'],
  ['LessThanOrEqual', '<='],
  ['Equal', '=='],
  ['NotEqual', '!=']
];

const operatorLabel = (operator) => OPERATORS.find(([name]) => name === operator)?.[1] ?? operator;

function renderPolicies(policies) {
  const body = document.querySelector('[data-policies-body]');
  if (!body || !Array.isArray(policies)) return;

  body.replaceChildren();
  if (policies.length === 0) {
    const row = document.createElement('tr');
    row.className = 'atlas-empty-row';
    const cell = document.createElement('td');
    cell.colSpan = 6;
    cell.textContent = 'No policies defined.';
    row.appendChild(cell);
    body.appendChild(row);
    return;
  }

  policies.forEach((policy) => {
    const row = document.createElement('tr');
    row.dataset.policyId = policy.id;

    const name = document.createElement('td');
    name.textContent = policy.name;

    const version = document.createElement('td');
    version.className = 'atlas-center atlas-mono';
    version.textContent = `v${policy.version}`;

    const state = document.createElement('td');
    const badge = document.createElement('span');
    badge.className = `atlas-badge atlas-badge--${policy.isActive ? 'ok' : 'neutral'}`;
    badge.textContent = policy.isActive ? 'ACTIVE' : 'INACTIVE';
    state.appendChild(badge);

    const conditions = document.createElement('td');
    (policy.conditions ?? []).forEach((condition) => {
      const tag = document.createElement('span');
      tag.className = 'atlas-tag atlas-mono';
      tag.textContent = `${condition.fieldName} ${operatorLabel(condition.operator)} ${condition.value}`;
      conditions.appendChild(tag);
    });

    const action = document.createElement('td');
    action.className = 'atlas-mono atlas-small';
    action.textContent = policy.action?.type ?? '—';

    const actions = document.createElement('td');
    actions.className = 'atlas-right atlas-nowrap';
    if (bootstrap.canManage) {
      const toggle = document.createElement('button');
      toggle.type = 'button';
      toggle.className = 'atlas-button atlas-button--ghost atlas-button--sm';
      toggle.textContent = policy.isActive ? 'DEACTIVATE' : 'ACTIVATE';
      toggle.addEventListener('click', async () => {
        const confirmed = await confirmAction({
          title: policy.isActive ? 'DEACTIVATE POLICY' : 'ACTIVATE POLICY',
          message: `Change the state of "${policy.name}" (v${policy.version})?`,
          hint: 'Policy changes are written to the append-only audit log.',
          confirmLabel: policy.isActive ? 'DEACTIVATE' : 'ACTIVATE'
        });
        if (!confirmed) return;
        const verb = policy.isActive ? 'deactivate' : 'activate';
        try {
          await post(`/api/v1/policies/${policy.id}/${verb}?organizationId=${organisationId}`, null);
          notifyOk('Policy updated', `${policy.name} → ${verb}d`);
          await refreshAll({ silent: true });
        } catch (error) {
          notifyFromError(error, 'Policy update failed');
        }
      });
      actions.appendChild(toggle);
    }

    row.append(name, version, state, conditions, action, actions);
    body.appendChild(row);
  });
}

async function refreshPolicies() {
  if (!organisationId) return;
  const policies = await get(`/api/v1/policies?organizationId=${organisationId}&page=1&pageSize=100`);
  renderPolicies(policies);
}

function conditionRow(container) {
  const row = document.createElement('div');
  row.className = 'atlas-row atlas-row--wrap';
  row.dataset.conditionRow = '';

  const field = document.createElement('input');
  field.className = 'atlas-input atlas-input--sm';
  field.name = 'fieldName';
  field.placeholder = 'error_rate';
  field.title = 'Fact field name — must exist in the evaluation facts supplied to the engine.';
  field.required = true;

  const operator = document.createElement('select');
  operator.className = 'atlas-select atlas-input--sm';
  operator.name = 'operator';
  OPERATORS.forEach(([name, symbol]) => {
    const option = document.createElement('option');
    option.value = name;
    option.textContent = symbol;
    operator.appendChild(option);
  });
  operator.value = 'GreaterThan';

  const value = document.createElement('input');
  value.className = 'atlas-input atlas-input--sm';
  value.name = 'value';
  value.type = 'number';
  value.step = 'any';
  value.required = true;

  const remove = document.createElement('button');
  remove.type = 'button';
  remove.className = 'atlas-button atlas-button--ghost atlas-button--sm';
  remove.textContent = '×';
  remove.title = 'Remove this condition';
  remove.addEventListener('click', () => row.remove());

  row.append(field, operator, value, remove);
  container.appendChild(row);
}

export async function init() {
  registerRefreshHandler('policies', refreshPolicies);
  try {
    await refreshPolicies();
  } catch (error) {
    notifyFromError(error, 'Policy listing failed');
  }

  const form = document.querySelector('[data-policy-form]');
  if (!form) return;

  if (!bootstrap.canManage) {
    form.classList.add('atlas-hidden');
    return;
  }

  const conditionList = form.querySelector('[data-condition-list]');
  conditionRow(conditionList);

  form.querySelector('[data-add-condition]')?.addEventListener('click', () => conditionRow(conditionList));

  const actionSelect = form.querySelector('[name="actionType"]');
  const severityField = form.querySelector('[data-severity-field]');
  const toggleSeverity = () => severityField?.classList.toggle('atlas-hidden', actionSelect.value !== 'RaiseAlert');
  actionSelect.addEventListener('change', toggleSeverity);
  toggleSeverity();

  bindForm(form, {
    onSubmit: async (payload) => {
      const conditions = [...form.querySelectorAll('[data-condition-row]')].map((row) => ({
        fieldName: row.querySelector('[name="fieldName"]').value.trim(),
        operator: row.querySelector('[name="operator"]').value,
        value: Number(row.querySelector('[name="value"]').value)
      }));
      if (conditions.length === 0) {
        reportStatus(form.querySelector('[data-policy-status]'), 'Add at least one condition.', 'warn');
        return;
      }

      const status = form.querySelector('[data-policy-status]');
      try {
        await post('/api/v1/policies', {
          organizationId: organisationId,
          name: payload.name,
          conditions,
          action: {
            type: actionSelect.value,
            alertSeverity: actionSelect.value === 'RaiseAlert' ? (payload.alertSeverity || 'Warning') : null
          }
        });
        notifyOk('Policy created', `${payload.name} saved as version 1.`);
        reportStatus(status, 'Created. New rules are active immediately; evaluation is read-only.', 'ok');
        form.reset();
        conditionList.replaceChildren();
        conditionRow(conditionList);
        toggleSeverity();
        await refreshPolicies();
      } catch (error) {
        reportStatus(status, error?.detail ?? error?.message ?? 'Policy creation failed.', 'error');
        notifyFromError(error, 'Policy creation failed');
      }
    },
    onError: (error) => notifyFromError(error, 'Policy creation failed')
  });
}
