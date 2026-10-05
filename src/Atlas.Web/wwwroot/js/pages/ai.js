/**
 * AI operations module.
 *
 * Two separated capabilities, exactly as the platform separates them:
 *   - READ: ask a question, which consults the selected read tools and answers
 *     only from their evidence ("Insufficient evidence." otherwise).
 *   - ACTION: run an Action-kind tool. Requires the PlatformAdmin role, an
 *     explicit in-UI confirmation, and the server independently re-checks both
 *     (ActionToolGuard) and audit-logs the attempt.
 *
 * The console never renders a generated sentence as if it were a measurement:
 * every answer lists the tool citations it was built from.
 */

import { post, readJsonIsland } from './../core/api-client.js';
import { notifyError, notifyOk, reportStatus } from './../core/notifications.js';
import { confirmAction } from './../core/modal.js';

const bootstrap = readJsonIsland('ai-bootstrap', { organizationId: '', canExecuteActions: false, tools: [] });
const organisationId = bootstrap.organizationId || (document.body.dataset.organizationId ?? '');
const tools = Array.isArray(bootstrap.tools) ? bootstrap.tools : [];
const readTools = tools.filter((tool) => tool.kind === 'Read');
const actionTools = tools.filter((tool) => tool.kind === 'Action');

function renderAnswer(answer) {
  const container = document.querySelector('[data-ai-answer]');
  const evidence = document.querySelector('[data-ai-evidence]');
  if (!container) return;

  container.textContent = answer?.text ?? 'Insufficient evidence.';
  container.classList.toggle('atlas-text-dim', answer?.hasSufficientEvidence === false);

  evidence?.replaceChildren();
  (answer?.citations ?? []).forEach((citation) => {
    const block = document.createElement('div');
    block.className = 'atlas-ai-evidence';
    const source = document.createElement('div');
    source.className = 'atlas-ai-evidence__source';
    source.textContent = citation.toolName ?? 'tool';
    const text = document.createElement('div');
    text.textContent = citation.description ?? '';
    const when = document.createElement('div');
    when.className = 'atlas-freshness';
    when.textContent = citation.observedAtUtc ? `observed ${new Date(citation.observedAtUtc).toLocaleString()}` : '';
    block.append(source, text, when);
    evidence.appendChild(block);
  });
}

async function ask(question) {
  if (!question || !organisationId) return;
  const selected = [...document.querySelectorAll('[data-tool-select]:checked')].map((input) => input.value);
  const container = document.querySelector('[data-ai-answer]');
  if (container) container.textContent = 'Consulting ATLAS read tools…';

  try {
    const answer = await post('/api/v1/ai/ask', {
      organizationId: organisationId,
      question,
      toolNames: selected.length > 0 ? selected : readTools.map((tool) => tool.name)
    });
    renderAnswer(answer);
  } catch (error) {
    if (container) container.textContent = error?.detail ?? error?.message ?? 'The assistant request failed.';
    notifyError('Assistant request failed', error?.detail ?? error?.message ?? 'Unknown error');
  }
}

function renderActionArguments() {
  const select = document.querySelector('[data-action-tool]');
  const argumentList = document.querySelector('[data-action-arguments]');
  if (!select || !argumentList) return;

  argumentList.replaceChildren();
  const tool = actionTools.find((candidate) => candidate.name === select.value);
  if (!tool) return;

  // Action tools take named arguments (e.g. DeactivatePolicy takes a
  // policyId). The console collects them as text; the server validates them —
  // an unknown or malformed argument fails the call, it is never guessed.
  ['policyId'].forEach((name) => {
    const field = document.createElement('label');
    field.className = 'atlas-field';
    const label = document.createElement('span');
    label.className = 'atlas-field__label';
    label.textContent = name;
    const input = document.createElement('input');
    input.className = 'atlas-input atlas-input--mono';
    input.name = name;
    input.required = true;
    input.placeholder = '00000000-0000-0000-0000-000000000000';
    field.append(label, input);
    argumentList.appendChild(field);
  });
}

export async function init() {
  const form = document.querySelector('[data-ai-form]');
  form?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const input = form.querySelector('[name="question"]');
    const question = (input?.value ?? '').trim();
    await ask(question);
  });

  document.querySelectorAll('[data-ai-prompt]').forEach((button) => {
    button.addEventListener('click', async () => {
      const input = document.querySelector('[data-ai-form] [name="question"]');
      const question = button.dataset.aiPrompt ?? '';
      if (input) input.value = question;
      await ask(question);
    });
  });

  const actionSelect = document.querySelector('[data-action-tool]');
  actionSelect?.addEventListener('change', renderActionArguments);
  renderActionArguments();

  document.querySelector('[data-action-form]')?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const status = form.querySelector('[data-action-status]');
    const toolName = form.querySelector('[name="toolName"]')?.value;
    const argumentsPayload = {};
    form.querySelectorAll('[data-action-arguments] input').forEach((input) => {
      if (input.value.trim()) argumentsPayload[input.name] = input.value.trim();
    });

    const confirmed = await confirmAction({
      title: 'RUN ACTION TOOL',
      message: `Execute "${toolName}" with ${JSON.stringify(argumentsPayload)}? This changes platform state.`,
      hint: 'PlatformAdmin only · the attempt is audit-logged whether it succeeds or is refused.',
      confirmLabel: 'EXECUTE'
    });
    if (!confirmed) return;

    try {
      const result = await post('/api/v1/ai/actions', {
        organizationId: organisationId,
        toolName,
        arguments: argumentsPayload,
        explicitConfirmation: true
      });
      notifyOk('Action completed', result?.summary ?? toolName);
      reportStatus(status, result?.summary ?? 'Action completed.', 'ok');
    } catch (error) {
      reportStatus(status, error?.detail ?? error?.message ?? 'Action refused.', 'error');
      notifyError('Action refused', error?.detail ?? error?.message ?? 'Unknown error');
    }
  });
}
