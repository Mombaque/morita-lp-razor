const terminalStatuses = new Set(['failed', 'cancelled', 'expired', 'refundpending', 'refunded']);
const activePaymentStates = new Set(['pending', 'processing', 'approved', 'conversionpending', 'cancellationpending']);
let stopActivePolling = null;

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

const replacePaymentFlow = (flow, html) => {
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
  flow.querySelectorAll('[data-payment-action][data-payment-method]').forEach((form) => {
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

  let attempts = 0;
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
    if (!stopped && attempts < 20 && document.visibilityState === 'visible' && timer === null) {
      timer = window.setTimeout(poll, 5000);
    }
  };

  const poll = async () => {
    timer = null;
    if (stopped || document.visibilityState === 'hidden' || attempts >= 20) return;
    attempts++;
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
      if (terminalStatuses.has(result.status)) {
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

  stopActivePolling?.();
  stopActivePolling = stop;
  document.addEventListener('visibilitychange', onVisibilityChange);
  schedule();
};

const bindCopyButton = (flow) => {
  const copy = flow.querySelector('[data-copy-pix]');
  copy?.addEventListener('click', async () => {
    const value = flow.querySelector('#pix-copy')?.value;
    if (!value) return;
    try {
      await navigator.clipboard.writeText(value);
      copy.textContent = 'Código copiado';
    } catch {
      copy.textContent = 'Selecione e copie o código';
    }
  });
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
      void submitPaymentAction(form, flow);
    });
  });

  bindCopyButton(flow);
  startPaymentPolling(flow);
};

initPaymentFlow(document.querySelector('[data-payment-flow]'));
