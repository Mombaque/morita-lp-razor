const MAX_MESSAGE_LENGTH = 1000;
const AI_NOTICE_VERSION = 'public-assistant-v1';
const PUBLISHED_SOURCE = 'PublishedCatalog';
const CONFIGURED_SOURCE = 'AssistantConfigured';

const state = {
  session: null,
  pendingMessage: null,
  sending: false,
  submitting: false,
  submissionAttempt: null,
  confirmationToken: null,
  previousFocus: null,
  initialProductSlug: null,
  cards: [],
  trackedSummary: null,
  sessionLoadVersion: 0,
  initialLoadPending: true,
  sessionRecoveryFailed: false,
};

const root = document.getElementById('public-assistant-root');
if (root) {
  root.innerHTML = `
    <button class="assistant-launcher" type="button" data-assistant-launcher aria-label="Abrir assistente Morita">🤖 <span>Encontrar produto</span></button>
    <div class="assistant-modal" data-assistant-modal aria-hidden="true">
      <div class="assistant-backdrop" data-assistant-close></div>
      <section class="assistant-panel" role="dialog" aria-modal="true" aria-labelledby="assistant-title">
        <header class="assistant-header"><div><p class="eyebrow">Morita</p><h2 id="assistant-title">Assistente de produtos</h2></div><button type="button" class="assistant-close" data-assistant-close aria-label="Fechar assistente">×</button></header>
        <div class="assistant-notice" data-assistant-notice hidden>
          <h3>Antes de começar</h3>
          <p>Este atendimento utiliza inteligência artificial. Suas mensagens serão processadas para ajudar na busca de produtos e armazenadas por até 30 dias. Evite compartilhar dados sensíveis.</p>
          <p data-assistant-start-status role="status"></p>
          <label class="assistant-honeypot" aria-hidden="true">Não preencha este campo<input type="text" name="website" tabindex="-1" autocomplete="off" data-assistant-website></label>
          <button type="button" class="button primary-cta" data-assistant-start>Iniciar conversa</button>
        </div>
        <div class="assistant-body" data-assistant-body role="log" aria-live="polite" aria-relevant="additions text"></div>
        <form class="assistant-input" data-assistant-input-form hidden>
          <label for="assistant-text" class="sr-only">Mensagem</label>
          <textarea id="assistant-text" maxlength="${MAX_MESSAGE_LENGTH}" rows="2" placeholder="Conte o que você procura..." data-assistant-text></textarea>
          <div class="assistant-input-footer"><span data-assistant-count>0/${MAX_MESSAGE_LENGTH}</span><button type="submit" class="button primary-cta" data-assistant-send>Enviar</button></div>
        </form>
        <div class="assistant-actions">
          <button type="button" class="button" data-assistant-reset hidden>Nova conversa</button>
          <button type="button" class="button" data-request-open data-assistant-fallback="form">Usar formulário</button>
          <a class="button whatsapp" href="https://wa.me/c/5515981079332" target="_blank" rel="noopener noreferrer" data-assistant-fallback="whatsapp" data-track-event="whatsapp_catalog_click" data-track-category="assistant">WhatsApp</a>
        </div>
      </section>
    </div>`;
  bindAssistant();
}

function bindAssistant() {
  document.addEventListener('click', handleFallbackClick, true);
  document.addEventListener('click', event => {
    if (!(event.target instanceof Element)) return;
    const opener = event.target.closest('[data-assistant-open]');
    if (opener) {
      state.initialProductSlug = opener.getAttribute('data-assistant-product');
      openAssistant();
      return;
    }
    if (event.target.closest('[data-assistant-launcher]')) {
      state.initialProductSlug = null;
      openAssistant();
    }
    if (event.target.closest('[data-assistant-close]')) closeAssistant();
    if (event.target.closest('[data-assistant-start]')) void startSession();
    if (event.target.closest('[data-assistant-reset]')) void resetSession();
  });
  document.addEventListener('keydown', handleKeydown);
  document.querySelector('[data-assistant-input-form]').addEventListener('submit', event => void sendMessage(event));
  document.querySelector('[data-assistant-text]').addEventListener('input', updateCount);
  document.querySelector('[data-assistant-start]').disabled = true;
  void loadSession().finally(() => {
    state.initialLoadPending = false;
    updateStartControl();
  });
}

function handleFallbackClick(event) {
  if (!(event.target instanceof Element)) return;
  const fallback = event.target.closest('[data-assistant-fallback]');
  if (!fallback) return;
  const fallbackType = fallback.getAttribute('data-assistant-fallback') || 'unknown';
  trackAssistant('assistant_fallback_used', fallbackType);
  if (fallbackType === 'form') closeAssistant();
}

async function loadSession(showFailure = false) {
  const loadVersion = ++state.sessionLoadVersion;
  try {
    const response = await fetch('/assistant/session', { headers: { Accept: 'application/json' } });
    if (loadVersion !== state.sessionLoadVersion) return false;
    if (response.ok) {
      state.session = await response.json();
      state.sessionRecoveryFailed = false;
      state.cards = [];
      state.submissionAttempt = null;
      updateStartControl();
      renderConversation();
      if (document.querySelector('[data-assistant-modal]')?.getAttribute('aria-hidden') === 'false') prefillProductQuestion();
      return true;
    }
    if (response.status === 404 || response.status === 410) {
      state.sessionRecoveryFailed = false;
      expireSession(response.status === 410 ? 'Esta conversa expirou. Inicie uma nova conversa.' : '');
      return false;
    }
    const message = await readError(response, 'Não foi possível recuperar a conversa agora.');
    if (!state.session) markSessionRecoveryFailure(message);
    else if (showFailure) showError(message);
  } catch {
    const message = 'Não foi possível recuperar a conversa agora.';
    if (!state.session) markSessionRecoveryFailure(message);
    else if (showFailure) showError(message);
  }
  return false;
}

function openAssistant() {
  const modal = document.querySelector('[data-assistant-modal]');
  state.previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  modal.setAttribute('aria-hidden', 'false');
  document.body.classList.add('assistant-open');
  trackAssistant('assistant_opened');
  if (state.session) {
    renderConversation();
    prefillProductQuestion();
  } else showStartState();
  document.querySelector('[data-assistant-close]').focus();
}

function prefillProductQuestion() {
  if (!state.initialProductSlug) return;
  const text = document.querySelector('[data-assistant-text]');
  text.value = `Quero saber sobre este produto: ${state.initialProductSlug}`.slice(0, MAX_MESSAGE_LENGTH);
  updateCount();
}

function closeAssistant() {
  const modal = document.querySelector('[data-assistant-modal]');
  if (!modal || modal.getAttribute('aria-hidden') === 'true') return;
  modal.setAttribute('aria-hidden', 'true');
  document.body.classList.remove('assistant-open');
  state.previousFocus?.focus();
  state.previousFocus = null;
}

function handleKeydown(event) {
  const modal = document.querySelector('[data-assistant-modal]');
  if (!modal || modal.getAttribute('aria-hidden') === 'true') return;
  if (event.key === 'Escape') {
    event.preventDefault();
    closeAssistant();
    return;
  }
  if (event.key !== 'Tab') return;
  const focusable = [...modal.querySelectorAll('button:not([disabled]), a[href], textarea:not([disabled]), input:not([disabled]):not([tabindex="-1"])')]
    .filter(element => element instanceof HTMLElement && element.offsetParent !== null);
  if (!focusable.length) return;
  if (event.shiftKey && document.activeElement === focusable[0]) {
    event.preventDefault();
    focusable.at(-1).focus();
  } else if (!event.shiftKey && document.activeElement === focusable.at(-1)) {
    event.preventDefault();
    focusable[0].focus();
  }
}

async function startSession() {
  const start = document.querySelector('[data-assistant-start]');
  if (start.disabled) return;
  if (state.sessionRecoveryFailed) {
    start.disabled = true;
    setStartStatus('Tentando recuperar sua conversa...');
    await loadSession(true);
    updateStartControl();
    return;
  }
  start.disabled = true;
  state.sessionLoadVersion++;
  clearStartStatus();
  try {
    const website = document.querySelector('[data-assistant-website]').value;
    const payload = {
      acceptedAiNotice: true,
      aiNoticeVersion: AI_NOTICE_VERSION,
      landingPage: window.location.pathname.slice(0, 500),
      campaign: new URLSearchParams(window.location.search).get('utm_campaign')?.slice(0, 150) || null,
      initialProductSlug: state.initialProductSlug || null,
      website,
    };
    let response = await postJson('/assistant/session', payload);
    let shouldPrefillProduct = false;
    if (response.status === 422 && payload.initialProductSlug && !website) {
      await response.body?.cancel();
      response = await postJson('/assistant/session', { ...payload, initialProductSlug: null });
      shouldPrefillProduct = response.ok;
    }
    if (!response.ok) {
      setStartStatus(await readError(response, 'Não foi possível iniciar o atendimento.'));
      return;
    }
    state.session = await response.json();
    state.sessionRecoveryFailed = false;
    state.cards = [];
    state.submissionAttempt = null;
    trackAssistant('assistant_session_started');
    renderConversation();
    const text = document.querySelector('[data-assistant-text]');
    if (shouldPrefillProduct && state.initialProductSlug) {
      text.value = `Quero saber sobre este produto: ${state.initialProductSlug}`.slice(0, MAX_MESSAGE_LENGTH);
      updateCount();
    }
    text.focus();
  } catch {
    setStartStatus('Não foi possível iniciar o atendimento. Tente novamente ou use uma das opções abaixo.');
  } finally {
    if (!state.session) updateStartControl();
  }
}

function renderConversation() {
  if (!state.session) return;
  const body = document.querySelector('[data-assistant-body]');
  const input = document.querySelector('[data-assistant-input-form]');
  document.querySelector('[data-assistant-notice]').hidden = true;
  document.querySelector('[data-assistant-reset]').hidden = false;
  body.textContent = '';
  for (const message of state.session.messages || []) appendMessage(body, message);
  renderDraft(body, state.session.draft);
  renderCards(body, state.cards);
  state.confirmationToken = null;
  if (state.session.actionType === 'Confirmation' && state.session.summary && state.session.confirmationToken) {
    renderConfirmation(body, state.session.summary, state.session.confirmationToken);
  }
  input.hidden = state.session.status === 'Submitted';
  body.scrollTop = body.scrollHeight;
}

function appendMessage(body, message) {
  const item = document.createElement('p');
  item.className = `assistant-message ${message.role === 'user' ? 'user' : 'assistant'}`;
  item.textContent = message.content || '';
  body.appendChild(item);
  return item;
}

function renderCards(body, cards) {
  if (!Array.isArray(cards) || !cards.length) return;
  const safeCards = cards.filter(card => card && (card.source === PUBLISHED_SOURCE || card.source === CONFIGURED_SOURCE));
  if (!safeCards.length) return;
  const wrapper = document.createElement('section');
  wrapper.className = 'assistant-products';
  const heading = document.createElement('h3');
  heading.textContent = 'Opções encontradas';
  wrapper.appendChild(heading);
  for (const card of safeCards) {
    const published = card.source === PUBLISHED_SOURCE;
    const article = document.createElement('article');
    article.className = 'assistant-product-card';
    const title = document.createElement('h4');
    title.textContent = card.name || 'Produto Morita';
    article.appendChild(title);
    const meta = document.createElement('p');
    meta.textContent = [card.modality, card.brand].filter(Boolean).join(' · ');
    article.appendChild(meta);
    if (published && card.price != null && typeof card.currency === 'string' && /^[A-Z]{3}$/.test(card.currency)) {
      const price = document.createElement('strong');
      try { price.textContent = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: card.currency }).format(Number(card.price)); }
      catch { price.textContent = ''; }
      if (price.textContent) article.appendChild(price);
    }
    const availability = document.createElement('p');
    availability.textContent = published
      ? 'Produto publicado no catálogo. A disponibilidade atual não pode ser garantida.'
      : 'A Morita trabalha com este produto, mas a disponibilidade não pode ser garantida.';
    article.appendChild(availability);
    if (published && typeof card.productPageUrl === 'string' && card.productPageUrl.startsWith('/products/') && !card.productPageUrl.startsWith('//')) {
      const link = document.createElement('a');
      link.href = card.productPageUrl;
      link.textContent = 'Ver produto';
      link.className = 'assistant-product-link';
      article.appendChild(link);
    }
    wrapper.appendChild(article);
  }
  body.appendChild(wrapper);
}

function renderDraft(body, draft) {
  const items = draft?.items;
  if (!Array.isArray(items) || !items.length) return;
  const card = document.createElement('section');
  card.className = 'assistant-draft';
  const heading = document.createElement('h3');
  heading.textContent = 'Consulta em andamento';
  card.appendChild(heading);
  const list = document.createElement('ul');
  for (const item of items) {
    const li = document.createElement('li');
    const brand = item.brand || (item.brandPreference === 1 || item.brandPreference === 'NoPreference' ? 'Sem preferência de marca' : null);
    li.textContent = [item.productType, item.modality, item.size, item.color, brand].filter(Boolean).join(' · ');
    list.appendChild(li);
  }
  card.appendChild(list);
  body.appendChild(card);
}

function renderConfirmation(body, summary, token) {
  state.confirmationToken = token;
  const card = document.createElement('section');
  card.className = 'assistant-confirmation';
  const text = document.createElement('p');
  text.className = 'assistant-summary';
  text.textContent = summary;
  card.appendChild(text);
  const form = document.createElement('form');
  form.innerHTML = `<label>Seu nome<input name="customerName" maxlength="120" autocomplete="name" required></label><label>WhatsApp<input name="customerWhatsapp" maxlength="40" inputmode="tel" autocomplete="tel" required></label><label class="assistant-consent"><input type="checkbox" name="acceptedPrivacyPolicy" required><span>Estou de acordo em compartilhar esses dados para que a Morita entre em contato comigo para prosseguir com o atendimento.</span></label><button class="button primary-cta" type="submit">Confirmar e enviar</button><p class="assistant-form-error" role="alert"></p>`;
  if (state.submissionAttempt?.payload?.confirmationToken === token && state.submissionAttempt.payload.expectedRevision === state.session?.draftRevision) {
    form.elements.customerName.value = state.submissionAttempt.payload.customerName;
    form.elements.customerWhatsapp.value = state.submissionAttempt.payload.customerWhatsapp;
    form.elements.acceptedPrivacyPolicy.checked = state.submissionAttempt.payload.acceptedPrivacyPolicy;
    form.querySelector('button[type="submit"]').textContent = 'Tentar novamente';
    lockSubmissionForm(form, true);
  }
  form.addEventListener('submit', event => void submitRequest(event));
  card.appendChild(form);
  body.appendChild(card);
  const summaryKey = `${state.session?.publicId || ''}:${state.session?.draftRevision || 0}`;
  if (state.trackedSummary !== summaryKey) {
    state.trackedSummary = summaryKey;
    trackAssistant('assistant_summary_presented');
  }
}

async function sendMessage(event) {
  event.preventDefault();
  const textControl = document.querySelector('[data-assistant-text]');
  const text = textControl.value.trim();
  if (!text || text.length > MAX_MESSAGE_LENGTH || state.sending || state.submitting || !state.session) return;
  state.sending = true;
  state.submissionAttempt = null;
  const messageId = state.pendingMessage?.text === text ? state.pendingMessage.id : crypto.randomUUID();
  state.pendingMessage = { id: messageId, text };
  textControl.disabled = true;
  document.querySelector('[data-assistant-send]').disabled = true;
  document.querySelector('[data-assistant-reset]').disabled = true;
  const body = document.querySelector('[data-assistant-body]');
  if (!body.querySelector(`[data-client-message-id="${messageId}"]`)) {
    appendMessage(body, { role: 'user', content: text }).dataset.clientMessageId = messageId;
  }
  const typing = document.createElement('p');
  typing.className = 'assistant-message assistant assistant-typing';
  typing.textContent = 'Consultando...';
  body.appendChild(typing);
  try {
    const response = await postJson('/assistant/message', { clientMessageId: messageId, expectedRevision: state.session.draftRevision, text });
    if (response.ok) {
      const turn = await response.json();
      state.session.messages = [...(state.session.messages || []), { role: 'user', content: text }, turn.message];
      state.session.draft = turn.draft;
      state.session.draftRevision = turn.draftRevision;
      state.session.actionType = turn.actionType;
      state.session.summary = turn.summary;
      state.session.confirmationToken = turn.confirmationToken;
      state.cards = Array.isArray(turn.catalogProducts) ? turn.catalogProducts : [];
      if (state.cards.length) trackAssistant('assistant_product_identified');
      state.pendingMessage = null;
      textControl.value = '';
      updateCount();
      renderConversation();
    } else if (response.status === 409) {
      await loadSession(true);
      if (state.session) showError('A conversa foi atualizada. Revise os dados e tente novamente.');
    } else if (response.status === 404 || response.status === 410) {
      expireSession('Esta conversa expirou. Inicie uma nova conversa.');
    } else {
      showError(await readError(response, 'Não foi possível responder agora. Tente novamente ou use uma das opções abaixo.'));
    }
  } catch {
    showError('Não foi possível responder agora. Sua mensagem foi preservada para uma nova tentativa.');
  } finally {
    typing.remove();
    state.sending = false;
    textControl.disabled = false;
    document.querySelector('[data-assistant-send]').disabled = false;
    document.querySelector('[data-assistant-reset]').disabled = false;
    if (state.session) textControl.focus();
  }
}

async function submitRequest(event) {
  event.preventDefault();
  if (state.submitting || state.sending || !state.session || !state.confirmationToken) return;
  const form = event.currentTarget;
  const button = form.querySelector('button[type="submit"]');
  const error = form.querySelector('.assistant-form-error');
  if (!state.submissionAttempt) {
    const data = new FormData(form);
    state.submissionAttempt = {
      key: crypto.randomUUID() + crypto.randomUUID(),
      payload: {
        confirmationToken: state.confirmationToken,
        expectedRevision: state.session.draftRevision,
        customerName: data.get('customerName'),
        customerWhatsapp: data.get('customerWhatsapp'),
        acceptedPrivacyPolicy: data.get('acceptedPrivacyPolicy') === 'on',
      },
    };
  }
  const attempt = state.submissionAttempt;
  state.submitting = true;
  button.disabled = true;
  document.querySelector('[data-assistant-text]').disabled = true;
  document.querySelector('[data-assistant-send]').disabled = true;
  document.querySelector('[data-assistant-reset]').disabled = true;
  error.textContent = '';
  lockSubmissionForm(form, true);
  trackAssistant('assistant_confirmation_started');
  try {
    const response = await postJson('/assistant/submit', attempt.payload, { 'Idempotency-Key': attempt.key });
    if (response.ok) {
      const submission = await response.json();
      state.submissionAttempt = null;
      state.session.status = 'Submitted';
      state.session.actionType = 'None';
      state.session.confirmationToken = null;
      state.confirmationToken = null;
      renderConversation();
      const requestNumber = Number.isInteger(submission.customerProductRequestId) ? ` Número da solicitação: ${submission.customerProductRequestId}.` : '';
      showSuccess(`Consulta enviada.${requestNumber} A equipe da Morita entrará em contato pelo WhatsApp.`);
      trackAssistant('assistant_request_submitted');
      return;
    }
    if (response.status === 404 || response.status === 410) {
      state.submissionAttempt = null;
      expireSession('Esta conversa expirou. Inicie uma nova conversa.');
      return;
    }
    if (response.status === 409) {
      state.submissionAttempt = null;
      await loadSession(true);
      if (state.session) showError('A conversa foi atualizada. Revise o resumo antes de confirmar novamente.');
      return;
    }
    const definitiveFailure = response.status === 400 || response.status === 422;
    error.textContent = await readError(response, 'Não foi possível enviar agora. Tente novamente ou fale pelo WhatsApp.');
    if (definitiveFailure) {
      state.submissionAttempt = null;
      lockSubmissionForm(form, false);
    } else {
      button.textContent = 'Tentar novamente';
    }
  } catch {
    error.textContent = 'A confirmação não foi concluída. Tente novamente com os mesmos dados ou fale pelo WhatsApp.';
    button.textContent = 'Tentar novamente';
  } finally {
    state.submitting = false;
    document.querySelector('[data-assistant-text]').disabled = false;
    document.querySelector('[data-assistant-send]').disabled = false;
    document.querySelector('[data-assistant-reset]').disabled = false;
    if (button.isConnected) button.disabled = false;
  }
}

function lockSubmissionForm(form, locked) {
  for (const input of form.querySelectorAll('input')) {
    if (input.type === 'checkbox') input.disabled = locked;
    else input.readOnly = locked;
  }
}

async function resetSession() {
  if (state.sending || state.submitting || !window.confirm('Encerrar esta conversa e iniciar uma nova?')) return;
  const reset = document.querySelector('[data-assistant-reset]');
  reset.disabled = true;
  try {
    const response = await postJson('/assistant/reset', {});
    if (!response.ok) {
      showError(await readError(response, 'Não foi possível encerrar a conversa agora.'));
      return;
    }
    clearLocalSession();
    showStartState('');
    document.querySelector('[data-assistant-start]').focus();
    trackAssistant('assistant_reset');
  } catch {
    showError('Não foi possível encerrar a conversa agora. Tente novamente.');
  } finally {
    reset.disabled = false;
  }
}

function clearLocalSession() {
  state.sessionLoadVersion++;
  state.session = null;
  state.sessionRecoveryFailed = false;
  state.cards = [];
  state.pendingMessage = null;
  state.submissionAttempt = null;
  state.confirmationToken = null;
  state.trackedSummary = null;
}

function expireSession(message) {
  clearLocalSession();
  showStartState(message);
}

function showStartState(message = null) {
  document.querySelector('[data-assistant-notice]').hidden = false;
  document.querySelector('[data-assistant-reset]').hidden = true;
  document.querySelector('[data-assistant-body]').textContent = '';
  document.querySelector('[data-assistant-input-form]').hidden = true;
  if (message !== null) setStartStatus(message);
  updateStartControl();
}

function markSessionRecoveryFailure(message) {
  state.sessionRecoveryFailed = true;
  showStartState(`${message} Tente recuperar a conversa antes de iniciar outra.`);
}

function updateStartControl() {
  const start = document.querySelector('[data-assistant-start]');
  start.disabled = state.initialLoadPending;
  start.textContent = state.sessionRecoveryFailed ? 'Tentar recuperar conversa' : 'Iniciar conversa';
}

function setStartStatus(message) {
  document.querySelector('[data-assistant-start-status]').textContent = message;
}

function clearStartStatus() { setStartStatus(''); }

function showError(message) {
  const body = document.querySelector('[data-assistant-body]');
  const error = document.createElement('p');
  error.className = 'assistant-error';
  error.setAttribute('role', 'alert');
  error.textContent = message;
  body.appendChild(error);
  body.scrollTop = body.scrollHeight;
}

function showSuccess(message) {
  const body = document.querySelector('[data-assistant-body]');
  const success = document.createElement('p');
  success.className = 'assistant-success';
  success.setAttribute('role', 'status');
  success.textContent = message;
  body.appendChild(success);
  body.scrollTop = body.scrollHeight;
}

function updateCount() {
  const text = document.querySelector('[data-assistant-text]');
  document.querySelector('[data-assistant-count]').textContent = `${text.value.length}/${MAX_MESSAGE_LENGTH}`;
}

function postJson(url, payload, extraHeaders = {}) {
  const token = document.querySelector('meta[name="request-verification-token"]')?.content;
  return fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: token || '', ...extraHeaders },
    body: JSON.stringify(payload),
  });
}

async function readError(response, fallback) {
  if (response.status === 410) return 'Esta conversa expirou. Inicie uma nova conversa.';
  if (response.status === 429) return 'Muitas tentativas. Aguarde um pouco.';
  try {
    const value = await response.json();
    return Array.isArray(value) ? value.join(' ') : fallback;
  } catch {
    return fallback;
  }
}

function trackAssistant(eventName, selectedCategory) {
  const payload = {
    event_category: 'assistant',
    page_path: window.location.pathname,
    page_title: document.title,
    destination_url: '',
    selected_category: selectedCategory,
  };
  if (typeof window.sendWebsiteUsageEvent === 'function') window.sendWebsiteUsageEvent(eventName, payload);
  else {
    window.dataLayer = window.dataLayer || [];
    window.dataLayer.push({ event: eventName, event_category: 'assistant', selected_category: selectedCategory });
  }
}
