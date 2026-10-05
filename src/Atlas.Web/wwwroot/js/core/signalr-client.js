/**
 * SignalR wiring for live operational updates (incident status, health lamps).
 *
 * Design decisions:
 *  - SignalR is used only where a push actually helps (incident and health
 *    events). Everything else refreshes on demand.
 *  - The browser client is loaded from the official CDN; when it is not
 *    available (offline/air-gapped deployment, blocked CDN) the module
 *    degrades to bounded polling of the same REST endpoints, so the console
 *    stays truthful about freshness instead of silently going dead.
 *  - The UI always shows the connection state so an operator can tell a live
 *    board from a stale one.
 */

import { get } from './api-client.js';

const HUB_URL = '/hubs/incidents';
const POLL_INTERVAL_MS = 20000;

const listeners = new Set();
let state = 'disconnected';
let connection = null;
let pollTimer = null;
let started = false;

function setState(next) {
  if (state === next) return;
  state = next;
  document.querySelectorAll('[data-atlas-live]').forEach((element) => {
    element.dataset.state = next;
    const label = element.querySelector('[data-atlas-live-label]');
    if (label) label.textContent = next === 'connected' ? 'LIVE' : next === 'connecting' ? 'CONNECTING' : 'POLLING';
  });
}

export const getState = () => state;

export function onIncidentUpdate(handler) {
  listeners.add(handler);
  return () => listeners.delete(handler);
}

function emit(payload) {
  listeners.forEach((handler) => {
    try {
      handler(payload);
    } catch {
      /* a broken listener must not break the stream */
    }
  });
}

/** Starts real-time delivery, or polling fallback, for the given organization. */
export async function start(organizationId, { fallbackUrl = null } = {}) {
  if (started) return;
  started = true;
  setState('connecting');

  const signalR = window.signalR;
  if (signalR) {
    try {
      connection = new signalR.HubConnectionBuilder()
        .withUrl(HUB_URL)
        .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

      connection.on('incidentChanged', (payload) => emit(payload));
      connection.on('serviceHealthChanged', (payload) => emit(payload));
      connection.onreconnecting(() => setState('connecting'));
      connection.onreconnected(() => setState('connected'));
      connection.onclose(() => {
        setState('disconnected');
        startPolling(organizationId, fallbackUrl);
      });

      await connection.start();
      setState('connected');
      return;
    } catch {
      // Hub unreachable — fall through to polling.
      connection = null;
    }
  }

  startPolling(organizationId, fallbackUrl);
}

function startPolling(organizationId, fallbackUrl) {
  const url = fallbackUrl ?? `/api/v1/incidents?organizationId=${encodeURIComponent(organizationId)}&page=1&pageSize=25`;
  if (pollTimer) return;

  setState('disconnected');
  const tick = async () => {
    try {
      const payload = await get(url);
      emit({ transport: 'poll', incidents: payload });
      setState('disconnected');
    } catch {
      setState('disconnected');
    }
  };

  tick();
  pollTimer = window.setInterval(tick, POLL_INTERVAL_MS);
}

export async function stop() {
  started = false;
  if (pollTimer) {
    window.clearInterval(pollTimer);
    pollTimer = null;
  }
  if (connection) {
    try {
      await connection.stop();
    } catch {
      /* already closed */
    }
    connection = null;
  }
  setState('disconnected');
}
