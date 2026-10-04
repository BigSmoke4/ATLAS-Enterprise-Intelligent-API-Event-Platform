/**
 * API management module: catalog inspection, route configuration (rate limit,
 * timeout, retry), service attribution and API-key issuance. All writes go
 * through the catalog application service; the UI never talks to a DbContext.
 */

import { get, post, readJsonIsland } from './../core/api-client.js';
import { notifyError, notifyFromError, notifyOk, reportStatus } from './../core/notifications.js';
import { registerRefreshHandler, refreshAll } from './../components/command-panel.js';
import { bindForm } from './../core/validation.js';
import { confirmAction } from './../core/modal.js';

const organisationId = document.body.dataset.organizationId ?? '';
const services = readJsonIsland('apis-services', []);
const apiKeys = readJsonIsland('apis-keys', []);

function renderRoutes(routes) {
  const body = document.querySelector('[data-routes-body]');
  if (!body) return;

  body.replaceChildren();
  if (!Array.isArray(routes) || routes.length === 0) {
    const row = document.createElement('tr');
    row.className = 'atlas-empty-row';
    const cell = document.createElement('td');
    cell.colSpan = 7;
    cell.textContent = 'No routes configured for this API version.';
    row.appendChild(cell);
    body.appendChild(row);
    return;
  }

  routes.forEach((route) => {
    const row = document.createElement('tr');
    row.dataset.routeId = route.id;

    const methodCell = document.createElement('td');
    const method = document.createElement('span');
    method.className = `atlas-method atlas-method--${(route.httpMethod ?? 'get').toLowerCase()}`;
    method.textContent = route.httpMethod;
    methodCell.appendChild(method);

    const pathCell = document.createElement('td');
    pathCell.className = 'atlas-route-path';
    pathCell.textContent = route.path;

    const limitCell = document.createElement('td');
    limitCell.className = 'is-numeric';
    limitCell.textContent = route.rateLimit ? `${route.rateLimit.limitPerWindow}/${Math.round(route.rateLimit.windowSeconds ?? 60)}s` : 'default';

    const timeoutCell = document.createElement('td');
    timeoutCell.className = 'is-numeric';
    timeoutCell.textContent = `${route.timeoutSeconds ?? route.timeout ?? '—'} s`;

    const retryCell = document.createElement('td');
    retryCell.className = 'is-numeric';
    retryCell.textContent = String(route.maxRetries ?? 0);

    const targetCell = document.createElement('td');
    targetCell.textContent = services.find((service) => service.id === route.targetServiceId)?.name ?? 'unattributed';

    const actionCell = document.createElement('td');
    const configure = document.createElement('button');
    configure.type = 'button';
    configure.className = 'atlas-button atlas-button--sm';
    configure.textContent = 'CONFIGURE';
    configure.addEventListener('click', () => loadRouteIntoForm(route));
    actionCell.appendChild(configure);

    row.append(methodCell, pathCell, limitCell, timeoutCell, retryCell, targetCell, actionCell);
    body.appendChild(row);
  });
}

function loadRouteIntoForm(route) {
  const form = document.querySelector('[data-route-form]');
  if (!form) return;
  form.querySelector('[name="routeId"]').value = route.id;
  form.querySelector('[name="limitPerWindow"]').value = route.rateLimit?.limitPerWindow ?? 100;
  form.querySelector('[name="windowSeconds"]').value = route.rateLimit?.windowSeconds ?? 60;
  form.querySelector('[name="scope"]').value = route.rateLimit?.scope ?? 'Ip';
  form.querySelector('[name="algorithm"]').value = route.rateLimit?.algorithm ?? 'FixedWindow';
  form.querySelector('[name="timeoutSeconds"]').value = route.timeoutSeconds ?? route.timeout ?? 30;
  form.querySelector('[name="maxRetries"]').value = route.maxRetries ?? 0;
  form.querySelector('[name="targetServiceId"]').value = route.targetServiceId ?? '';
  form.querySelector('[data-route-label]').textContent = `${route.httpMethod} ${route.path}`;
}

async function loadRoutes(versionId) {
  if (!organisationId || !versionId) return;
  const routes = await get(`/api/v1/apis/versions/${versionId}/routes?organizationId=${organisationId}`);
  renderRoutes(routes);
}

async function refreshCatalog() {
  if (!organisationId) return;
  const apis = await get(`/api/v1/apis?organizationId=${organisationId}&page=1&pageSize=200`);
  const container = document.querySelector('[data-api-catalog]');
  if (!container || !Array.isArray(apis)) return;

  container.replaceChildren();
  if (apis.length === 0) {
    container.innerHTML = '<div class="atlas-empty"><span class="atlas-empty__glyph">0</span><span class="atlas-empty__title">No APIs registered</span><span class="atlas-empty__hint">Register an API to start collecting route-level telemetry.</span></div>';
    return;
  }

  apis.forEach((api) => {
    const card = document.createElement('div');
    card.className = 'atlas-api-card';
    card.dataset.apiId = api.id;

    const head = document.createElement('div');
    head.className = 'atlas-api-card__head';
    const name = document.createElement('span');
    name.className = 'atlas-api-card__name';
    name.textContent = api.name;
    const badge = document.createElement('span');
    badge.className = `atlas-badge ${api.isActive ? 'atlas-badge--ok' : 'atlas-badge--neutral'}`;
    badge.textContent = api.isActive ? 'ACTIVE' : 'INACTIVE';
    head.append(name, badge);

    const path = document.createElement('span');
    path.className = 'atlas-api-card__path';
    path.textContent = api.basePath;

    const meta = document.createElement('div');
    meta.className = 'atlas-api-card__meta';
    meta.textContent = `${api.versionCount} version(s)`;

    card.append(head, path, meta);
    card.addEventListener('click', async () => {
      container.querySelectorAll('.atlas-api-card').forEach((element) => element.classList.remove('is-selected'));
      card.classList.add('is-selected');
      document.querySelector('[data-selected-api]').textContent = `${api.name} (${api.basePath})`;
      document.querySelector('[data-version-picker]').dataset.apiId = api.id;
      const versions = await loadVersions(api.id);
      if (versions.length > 0) await loadRoutes(versions[0].id);
      else renderRoutes([]);
    });
    container.appendChild(card);
  });
}

async function loadVersions(apiId) {
  const versions = await get(`/api/v1/apis/${apiId}/versions?organizationId=${organisationId}`).catch(() => null);
  const picker = document.querySelector('[data-version-picker]');
  if (!picker) return [];

  if (versions === null) {
    // Say why the picker is empty instead of silently showing nothing: an
    // empty version list and a failed request look identical otherwise.
    picker.replaceChildren();
    renderRoutes([]);
    const body = document.querySelector('[data-routes-body]');
    if (body) {
      body.replaceChildren();
      const row = document.createElement('tr');
      row.className = 'atlas-empty-row';
      const cell = document.createElement('td');
      cell.colSpan = 7;
      cell.textContent = 'Version list could not be loaded for this API.';
      row.appendChild(cell);
      body.appendChild(row);
    }
    return [];
  }

  picker.replaceChildren();
  const list = Array.isArray(versions) ? versions : [];
  if (list.length === 0) return [];

  list.forEach((version) => {
    const option = document.createElement('option');
    option.value = version.id ?? version.versionId;
    option.textContent = `v${version.versionNumber ?? '?'}`;
    picker.appendChild(option);
  });
  return list;
}

export async function init() {
  registerRefreshHandler('api-catalog', refreshCatalog);
  registerRefreshHandler('api-keys', refreshKeys);
  await refreshCatalog();
  await refreshKeys();

  document.querySelector('[data-version-picker]')?.addEventListener('change', async (event) => {
    await loadRoutes(event.target.value);
  });

  const createApi = document.querySelector('[data-create-api-form]');
  if (createApi) {
    bindForm(createApi, {
      onSubmit: async (payload) => {
        await post('/api/v1/apis', {
          organizationId: organisationId,
          name: payload.name,
          basePath: payload.basePath
        });
        notifyOk('API registered', payload.name);
        createApi.reset();
        await refreshAll({ silent: true });
      },
      onError: (error) => notifyFromError(error, 'API registration failed')
    });
  }

  const routeForm = document.querySelector('[data-route-form]');
  routeForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const status = form.querySelector('[data-route-status]');
    const routeId = form.querySelector('[name="routeId"]').value;
    if (!routeId) {
      reportStatus(status, 'Select a route from the table first.', 'warn');
      return;
    }

    const body = {
      organizationId: organisationId,
      rateLimit: {
        limitPerWindow: Number(form.querySelector('[name="limitPerWindow"]').value),
        window: `00:${String(Number(form.querySelector('[name="windowSeconds"]').value)).padStart(2, '0')}:00`,
        scope: form.querySelector('[name="scope"]').value,
        algorithm: form.querySelector('[name="algorithm"]').value
      },
      timeout: `00:00:${String(Number(form.querySelector('[name="timeoutSeconds"]').value)).padStart(2, '0')}`,
      maxRetries: Number(form.querySelector('[name="maxRetries"]').value)
    };

    try {
      await post(`/api/v1/apis/routes/${routeId}/configure`, body);

      const targetServiceId = form.querySelector('[name="targetServiceId"]').value;
      await put(`/api/v1/apis/routes/${routeId}/target-service`, { organizationId: organisationId, serviceId: targetServiceId || null });

      notifyOk('Route configured', 'Rate limit, timeout and retry policy saved.');
      reportStatus(status, 'Saved. The next request uses the new policy.', 'ok');
      const versionId = document.querySelector('[data-version-picker]')?.value;
      await loadRoutes(versionId);
    } catch (error) {
      reportStatus(status, error?.detail ?? error?.message ?? 'Save failed.', 'error');
      notifyFromError(error, 'Route configuration failed');
    }
  });

  document.querySelector('[data-issue-key-form]')?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const name = form.querySelector('[name="name"]')?.value?.trim();
    if (!name || !organisationId) return;

    try {
      const result = await post('/api/v1/account/api-keys', {
        organizationId: organisationId,
        name,
        expiresAtUtc: null
      });
      const reveal = document.querySelector('[data-key-reveal]');
      if (reveal) {
        reveal.classList.remove('atlas-hidden');
        // The raw key is returned exactly once; there is no endpoint that can
        // return it again, so this is the operator's only chance to copy it.
        reveal.textContent = result?.rawKey ?? 'Key issued, but the API did not return the plaintext value — check the identity service.';
      }
      notifyOk('API key issued', 'The key is shown once. Store it in your secret manager.');
      form.reset();
      await refreshKeys();
    } catch (error) {
      notifyError('API key issuance failed', error?.detail ?? error?.message ?? 'Unknown error');
    }
  });

  document.querySelector('[data-keys-body]')?.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-revoke-key]');
    if (!button || !organisationId) return;
    const confirmed = await confirmAction({
      title: 'REVOKE API KEY',
      message: `Revoke key ${button.dataset.keyPrefix}? Clients using it will immediately receive 401.`,
      confirmLabel: 'REVOKE'
    });
    if (!confirmed) return;
    try {
      await post(`/api/v1/account/api-keys/${button.dataset.revokeKey}/revoke?organizationId=${organisationId}`, null);
      notifyOk('API key revoked', button.dataset.keyPrefix ?? '');
      await refreshKeys();
    } catch (error) {
      notifyFromError(error, 'Revocation failed');
    }
  });

  if (apiKeys.length === 0) {
    document.querySelectorAll('[data-keys-empty]').forEach((element) => element.classList.remove('atlas-hidden'));
  }
}

/**
 * Re-renders the key table from the API. Key metadata (never the secret) is
 * the only thing the listing endpoint returns, so this is safe to render.
 */
async function refreshKeys() {
  const body = document.querySelector('[data-keys-body]');
  if (!body || !organisationId) return;

  let keys;
  try {
    keys = await get(`/api/v1/account/api-keys?organizationId=${organisationId}`);
  } catch (error) {
    if (error?.status === 403) return; // role cannot read keys: leave the server-rendered state alone
    throw error;
  }

  body.replaceChildren();
  if (!Array.isArray(keys) || keys.length === 0) {
    const row = document.createElement('tr');
    row.className = 'atlas-empty-row';
    row.dataset.keysEmpty = 'true';
    const cell = document.createElement('td');
    cell.colSpan = 6;
    cell.textContent = 'No API keys issued for this organization.';
    row.appendChild(cell);
    body.appendChild(row);
    return;
  }

  keys.forEach((key) => {
    const row = document.createElement('tr');
    [key.name, `${key.prefix}…`, key.isRevoked ? 'REVOKED' : 'ACTIVE',
      new Date(key.createdAtUtc).toLocaleString(),
      key.lastUsedAtUtc ? new Date(key.lastUsedAtUtc).toLocaleString() : 'never']
      .forEach((value, index) => {
        const cell = document.createElement('td');
        cell.textContent = value;
        if (index === 1) cell.className = 'atlas-mono atlas-small';
        row.appendChild(cell);
      });

    const actions = document.createElement('td');
    actions.className = 'atlas-right';
    if (!key.isRevoked) {
      const revoke = document.createElement('button');
      revoke.type = 'button';
      revoke.className = 'atlas-button atlas-button--danger atlas-button--sm';
      revoke.textContent = 'REVOKE';
      revoke.dataset.revokeKey = key.id;
      revoke.dataset.keyPrefix = key.prefix;
      actions.appendChild(revoke);
    }
    row.appendChild(actions);
    body.appendChild(row);
  });
}

async function put(url, body) {
  const { put: putRequest } = await import('./../core/api-client.js');
  return putRequest(url, body);
}
