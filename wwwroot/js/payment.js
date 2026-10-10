const terminalStatuses = new Set(['failed', 'cancelled', 'expired', 'refundpending', 'refunded']);
const activePaymentStates = new Set(['pending', 'unknown', 'processing', 'approved', 'conversionpending', 'cancellationpending']);
const FAST_POLL_MS = 5000;
const SLOW_POLL_MS = 15000;
const FAST_POLL_WINDOW_MS = 2 * 60 * 1000;
// Without an expiry on the page, stop polling after this long; the shopper can still check manually.
const FALLBACK_POLL_WINDOW_MS = 30 * 60 * 1000;
let stopActivePolling = null;
let activeCardForm = null;

const setActionBusy = (form, busy) => {
  form.dataset.paymentBusy = busy ? 'true' : 'false';
  form.setAttribute('aria-busy', String(busy));
  const button = form.querySelector('button[type="submit"]');
  if (button) button.disabled = busy;
};

const showPaymentError = (flow, message) => {
  let feedback = flow.querySelector('[data-payment-feedback]');
  if (!feedback) {
    feedback = document.createElement('p');
    flow.prepend(feedback);
  }
  feedback.className = 'validation-error';
  feedback.setAttribute('role', 'alert');
  feedback.dataset.paymentFeedback = '';
  feedback.textContent = message;
};

const unmountCardForm = () => {
  try {
    activeCardForm?.unmount();
  } catch {
    // The provider may already have torn the form down.
  }
  activeCardForm = null;
};

const replacePaymentFlow = (flow, html) => {
  unmountCardForm();
  const template = document.createElement('template');
  template.innerHTML = html.trim();
  const replacement = template.content.querySelector('[data-payment-flow]');
  if (!replacement) throw new Error('Payment flow fragment was not returned');
  flow.replaceWith(replacement);
  initPaymentFlow(replacement);
  return replacement;
};

const updatePaymentMethodUrl = (method) => {
  const url = new URL(window.location.href);
  url.searchParams.set('paymentMethod', method);
  window.history.replaceState(window.history.state, '', url);
};

const setPaymentMethod = (flow, method, updateUrl = true) => {
  const inputs = [...flow.querySelectorAll('input[name="paymentMethod"]')];
  const selected = method || inputs.find((input) => input.checked)?.value;
  if (!selected) return;

  inputs.forEach((input) => { input.checked = input.value === selected; });
  flow.querySelectorAll('[data-payment-action][data-payment-method], [data-payment-panel][data-payment-method]').forEach((form) => {
    form.hidden = form.dataset.paymentMethod !== selected;
  });
  if (updateUrl) updatePaymentMethodUrl(selected);
};

const readJsonRedirect = async (response) => {
  const payload = await response.json();
  if (!response.ok) throw new Error(payload?.message || 'Payment action failed');
  if (!payload?.redirectUrl) throw new Error('Payment redirect was not returned');
  return payload.redirectUrl;
};

const submitPaymentAction = async (form, flow) => {
  if (form.dataset.paymentBusy === 'true') return;

  setActionBusy(form, true);
  try {
    const data = new FormData(form);
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    if (token && !data.get('__RequestVerificationToken')) data.set('__RequestVerificationToken', token);
    const response = await fetch(form.action, {
      method: 'POST',
      body: data,
      credentials: 'same-origin',
      headers: {
        Accept: 'text/html, application/json',
        'X-Requested-With': 'XMLHttpRequest'
      }
    });
    const contentType = response.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
      const redirectUrl = await readJsonRedirect(response);
      window.location.assign(redirectUrl);
      return;
    }

    const html = await response.text();
    if (!response.ok) throw new Error(`Payment action failed with status ${response.status}`);
    replacePaymentFlow(flow, html);
  } catch {
    if (document.contains(form)) setActionBusy(form, false);
    showPaymentError(flow, 'Não foi possível atualizar o pagamento. Tente novamente.');
  }
};

const refreshPaymentFlow = async (flow) => {
  const response = await fetch(`${window.location.pathname}?handler=Payment&format=fragment`, {
    credentials: 'same-origin',
    headers: {
      Accept: 'text/html, application/json',
      'X-Requested-With': 'XMLHttpRequest'
    }
  });
  const contentType = response.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    const redirectUrl = await readJsonRedirect(response);
    window.location.assign(redirectUrl);
    return;
  }

  const html = await response.text();
  if (!response.ok) throw new Error(`Payment refresh failed with status ${response.status}`);
  replacePaymentFlow(flow, html);
};

const startPaymentPolling = (flow) => {
  const card = flow.querySelector('[data-payment-status]');
  if (!card || !activePaymentStates.has(card.dataset.paymentStatus)) return;

  const startedAt = Date.now();
  const expiresAt = Date.parse(flow.querySelector('[data-expires-at]')?.dataset.expiresAt ?? '');
  const pollUntil = Number.isFinite(expiresAt) ? expiresAt : startedAt + FALLBACK_POLL_WINDOW_MS;
  const checkButton = flow.querySelector('[data-check-payment]');
  const checkStatus = flow.querySelector('[data-check-payment-status]');
  let timer = null;
  let stopped = false;

  const stop = () => {
    stopped = true;
    if (timer !== null) window.clearTimeout(timer);
    timer = null;
    document.removeEventListener('visibilitychange', onVisibilityChange);
    if (stopActivePolling === stop) stopActivePolling = null;
  };

  const schedule = () => {
    // Keep polling a little past the expiry so the server's "expired" status swaps in the "Gerar novo PIX" action.
    if (stopped || document.visibilityState !== 'visible' || timer !== null || Date.now() > pollUntil + 60000) return;
    const delay = Date.now() - startedAt < FAST_POLL_WINDOW_MS ? FAST_POLL_MS : SLOW_POLL_MS;
    timer = window.setTimeout(poll, delay);
  };

  const poll = async () => {
    if (timer !== null) window.clearTimeout(timer);
    timer = null;
    if (stopped || document.visibilityState === 'hidden') return;
    try {
      const response = await fetch(`${window.location.pathname}?handler=Payment`, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
      });
      if (!response.ok) {
        schedule();
        return;
      }
      const result = await response.json();
      if (result.state === 'converted' && (result.redirectUrl || result.url)) {
        window.location.assign(result.redirectUrl || result.url);
        return;
      }
      if (terminalStatuses.has(result.status) || (card.dataset.awaitingCode === 'true' && result.awaitingCode === false) || (card.dataset.paymentStatus === 'unknown' && result.status && result.status !== 'unknown')) {
        try {
          await refreshPaymentFlow(flow);
        } catch {
          schedule();
        }
        return;
      }
      schedule();
    } catch {
      schedule();
    }
  };

  const onVisibilityChange = () => {
    if (document.visibilityState === 'visible' && timer === null) void poll();
  };

  checkButton?.addEventListener('click', async () => {
    checkButton.disabled = true;
    if (checkStatus) checkStatus.textContent = 'Verificando pagamento...';
    await poll();
    if (stopped) return;
    checkButton.disabled = false;
    if (checkStatus) checkStatus.textContent = 'Ainda não recebemos a confirmação. Se você já pagou, aguarde alguns instantes.';
  });

  stopActivePolling?.();
  stopActivePolling = stop;
  document.addEventListener('visibilitychange', onVisibilityChange);
  schedule();
};

const bindCopyButton = (flow) => {
  const copy = flow.querySelector('[data-copy-pix]');
  const label = copy?.textContent ?? '';
  copy?.addEventListener('click', async () => {
    const value = flow.querySelector('#pix-copy')?.value;
    if (!value) return;
    try {
      await navigator.clipboard.writeText(value);
      copy.textContent = 'Código copiado';
    } catch {
      copy.textContent = 'Selecione e copie o código';
    }
    window.setTimeout(() => { copy.textContent = label; }, 3000);
  });
};

const formatRemaining = (milliseconds) => {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = String(totalSeconds % 60).padStart(2, '0');
  return `Restam ${minutes}:${seconds}.`;
};

let countdownTimer = null;
const startCountdown = (flow) => {
  window.clearInterval(countdownTimer);
  const output = flow.querySelector('[data-pix-countdown]');
  const expiresAt = Date.parse(flow.querySelector('[data-expires-at]')?.dataset.expiresAt ?? '');
  if (!output || !Number.isFinite(expiresAt)) return;
  const tick = () => {
    const remaining = expiresAt - Date.now();
    output.textContent = remaining > 0 ? formatRemaining(remaining) : 'Código expirado.';
    if (remaining <= 0) window.clearInterval(countdownTimer);
  };
  tick();
  countdownTimer = window.setInterval(tick, 1000);
};

const submitEmbeddedCard = async (panel, flow, payload) => {
  const token = document.querySelector('meta[name="request-verification-token"]')?.content;
  const response = await fetch(panel.dataset.submitUrl, {
    method: 'POST',
    body: JSON.stringify(payload),
    credentials: 'same-origin',
    headers: {
      Accept: 'text/html, application/json',
      'Content-Type': 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
      ...(token ? { RequestVerificationToken: token } : {})
    }
  });
  const contentType = response.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    window.location.assign(await readJsonRedirect(response));
    return;
  }

  const html = await response.text();
  if (!response.ok) throw new Error(`Card payment failed with status ${response.status}`);
  replacePaymentFlow(flow, html);
};

const initCardForm = async (flow) => {
  const panel = flow.querySelector('[data-card-form]');
  if (!panel) return;
  const loading = panel.querySelector('[data-card-loading]');
  panel.addEventListener('card-form-ready', () => { if (loading) loading.hidden = true; });
  try {
    const { mountCardForm } = await import('./payment-providers/index.js');
    if (!document.contains(panel)) return;
    activeCardForm = await mountCardForm(panel, async (payload) => {
      try {
        await submitEmbeddedCard(panel, flow, payload);
      } catch (error) {
        showPaymentError(flow, 'Não foi possível enviar o pagamento com cartão. Tente novamente.');
        throw error;
      }
    });
  } catch {
    if (loading) loading.hidden = true;
    showPaymentError(flow, 'O formulário do cartão não carregou. Atualize a página ou pague com PIX.');
  }
};

const initCardChallenge = (flow) => {
  const challenge = flow.querySelector('[data-card-challenge]');
  if (!challenge || challenge.querySelector('iframe')) return;
  const name = `card-challenge-${Date.now()}`;
  const frame = document.createElement('iframe');
  frame.name = name;
  frame.title = 'Verificação do banco emissor';
  frame.className = 'card-challenge-frame';
  const form = document.createElement('form');
  form.method = 'POST';
  form.action = challenge.dataset.challengeUrl;
  form.target = name;
  form.hidden = true;
  const creq = document.createElement('input');
  creq.type = 'hidden';
  creq.name = 'creq';
  creq.value = challenge.dataset.challengeCreq;
  form.append(creq);
  challenge.append(frame, form);
  form.submit();
};

const initPaymentFlow = (flow) => {
  if (!(flow instanceof HTMLElement)) return;
  stopActivePolling?.();

  const methodInputs = [...flow.querySelectorAll('input[name="paymentMethod"]')];
  methodInputs.forEach((input) => input.addEventListener('change', () => setPaymentMethod(flow, input.value)));
  setPaymentMethod(flow, undefined, false);

  flow.querySelectorAll('[data-payment-action]').forEach((form) => {
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) return;
      void submitPaymentAction(form, flow);
    });
  });

  bindCopyButton(flow);
  startCountdown(flow);
  startPaymentPolling(flow);
  initCardChallenge(flow);
  void initCardForm(flow);
};

initPaymentFlow(document.querySelector('[data-payment-flow]'));
