import { ADULT_KIMONO_SIZE_CHART, KIDS_KIMONO_SIZE_CHART, suggestKimonoSize } from './kimono-size.js';

const API_BASE_URL = window.API_BASE_URL || (window.location.hostname === 'localhost'
  ? 'http://localhost:5001'
  : 'https://morita-api-1nnj.onrender.com');

const WHATSAPP_PHONE = window.MORITA_WHATSAPP_PHONE || '5515981079332';

const STEPS = ['Produtos', 'Detalhes', 'Contato'];
const TOTAL_STEPS = STEPS.length;

// Morita API accepts between 1 and 10 items per request.
const MAX_ITEMS = 10;
const MAX_QUANTITY = 20;
const DETAILS_MAX_LENGTH = 400;
const NOTES_MAX_LENGTH = 600;
const DRAFT_STORAGE_KEY = 'morita.requestDraft.v1';
const DRAFT_SAVE_DELAY_MS = 400;

const FOCUSABLE_SELECTOR = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]):not(.request-honeypot), textarea:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

const PAGE_CATEGORY = {
  jiuJitsu: 'jiu-jitsu',
  muayThai: 'muay-thai',
};

const WHATSAPP_FLOAT_MESSAGES = {
  [PAGE_CATEGORY.jiuJitsu]: 'Olá, Morita! Vim pelo site e quero ajuda para escolher equipamentos de Jiu-Jitsu.',
  [PAGE_CATEGORY.muayThai]: 'Olá, Morita! Vim pelo site e quero ajuda para escolher equipamentos de Muay Thai / Boxe.',
  default: 'Olá, Morita! Vim pelo site e quero ajuda para escolher meu equipamento.',
};

const REQUEST_STATUS = {
  idle: 'idle',
  success: 'success',
};

const BUTTON_LABELS = {
  continue: 'Continuar',
  submit: 'Enviar consulta',
  submitting: 'Enviando...',
};

const ERROR_MESSAGES = {
  products: 'Selecione pelo menos um produto para continuar.',
  required: 'Preencha seu nome e WhatsApp para continuar.',
  phone: 'Informe um WhatsApp com DDD, por exemplo (15) 99999-9999.',
  privacy: 'Confirme o uso dos seus dados para enviar a consulta.',
  maxItems: `Cada consulta aceita até ${MAX_ITEMS} itens. Para mais, fale com a gente pelo WhatsApp.`,
  submit: 'Não foi possível enviar agora. Envie a mesma consulta pelo WhatsApp:',
};

const FIELD = {
  productTypes: 'productTypes',
  productDetails: 'productDetails',
  size: 'size',
  color: 'color',
  brand: 'brand',
  audience: 'audience',
  sleeve: 'sleeve',
  quantity: 'quantity',
  heightCm: 'heightCm',
  weightKg: 'weightKg',
  age: 'age',
  customerName: 'customerName',
  customerPhone: 'customerPhone',
  deliveryMethod: 'deliveryMethod',
  deliveryArea: 'deliveryArea',
  deadline: 'deadline',
  notifyRestock: 'notifyRestock',
  notes: 'notes',
  website: 'website',
  acceptedPrivacyPolicy: 'acceptedPrivacyPolicy',
};

const DETAIL_FIELD_SEPARATOR = '::';

const CHOICE_LAYOUT = {
  cards: '',
  compact: 'request-choice-grid-compact',
  chips: 'request-choice-grid-chips',
};
const EXTRA_ITEM_PREFIX = '#';

const MODALITY = {
  jiuJitsu: 'Jiu-Jitsu',
  muayThaiBoxe: 'Muay Thai / Boxe',
  mma: 'MMA',
  judo: 'Judô',
};

const PRODUCT_TYPE = {
  kimonoAdulto: 'Kimono Adulto',
  kimonoInfantilJudo: 'Kimono Infantil / Judô',
  faixaAdulto: 'Faixa adulto',
  faixaInfantil: 'Faixa infantil',
  rashguard: 'Rashguard',
  bermudaShorts: 'Bermuda / shorts',
  luvas: 'Luvas',
  bandagem: 'Bandagem',
  protetorBucal: 'Protetor bucal',
  caneleira: 'Caneleira',
  outro: 'Outro',
};

const NO_BRAND_PREFERENCE = 'Sem preferência';

// Mirrors Morita API CustomerProductRequestBrandPreference.
const BRAND_PREFERENCE = {
  notInformed: 0,
  noPreference: 1,
  specific: 2,
};

const DELIVERY_METHOD = {
  pickup: 'Retirar na loja',
  delivery: 'Receber em casa',
};

const DEADLINES = ['O quanto antes', 'Graduação / exame de faixa', 'Campeonato', 'Sem pressa'];

const UNKNOWN_SIZE = 'Não sei';
const ADULT_KIMONO_SIZES = ['F2', 'F3', 'A0', 'A1', 'A2', 'A3', 'A4', 'A5', 'A6'];
const INFANTIL_JUDO_SIZES = ['M000', 'M00', 'M0', 'M1', 'M2', 'M3', 'M4'];
const CLOTHING_SIZES = ['PP', 'P', 'M', 'G', 'GG', 'XGG'];
const GLOVE_SIZES = ['8oz', '10oz', '12oz', '14oz', '16oz'];
const SHIN_GUARD_SIZES = ['P', 'M', 'G', 'GG'];
const SHIN_GUARD_COLORS = ['Azul', 'Vermelha', 'Outra'];
const KIMONO_COLORS = ['Branco', 'Azul', 'Preto', 'Cinza', 'Outra'];
const GLOVE_COLORS = ['Preta', 'Vermelha', 'Azul', 'Rosa', 'Branca', 'Outra'];
const RASHGUARD_AUDIENCES = ['Masculina', 'Feminina', 'Infantil'];
const SLEEVES = ['Manga longa', 'Manga curta'];
const BELT_ICON_BASE_PATH = '/icons/faixas/';
const ADULT_BELT_COLORS = ['Branca', 'Azul', 'Roxa', 'Marrom', 'Preta', 'Outra'];
const JIU_JITSU_KIDS_BELT_COLORS = [
  'Branca',
  'Cinza e branca',
  'Cinza',
  'Cinza e preta',
  'Amarela e branca',
  'Amarela',
  'Amarela e preta',
  'Laranja e branca',
  'Laranja',
  'Laranja e preta',
  'Verde e branca',
  'Verde',
  'Verde e preta',
];
const JUDO_KIDS_BELT_COLORS = ['Amarela', 'Laranja', 'Verde'];

const productTypesByModality = {
  [MODALITY.jiuJitsu]: [PRODUCT_TYPE.kimonoAdulto, PRODUCT_TYPE.kimonoInfantilJudo, PRODUCT_TYPE.faixaAdulto, PRODUCT_TYPE.faixaInfantil, PRODUCT_TYPE.rashguard, PRODUCT_TYPE.bermudaShorts],
  [MODALITY.muayThaiBoxe]: [PRODUCT_TYPE.luvas, PRODUCT_TYPE.caneleira, PRODUCT_TYPE.bermudaShorts, PRODUCT_TYPE.bandagem, PRODUCT_TYPE.protetorBucal, PRODUCT_TYPE.outro],
  [MODALITY.mma]: [PRODUCT_TYPE.bermudaShorts, PRODUCT_TYPE.rashguard, PRODUCT_TYPE.luvas, PRODUCT_TYPE.outro],
  [MODALITY.judo]: [PRODUCT_TYPE.kimonoInfantilJudo, PRODUCT_TYPE.faixaAdulto, PRODUCT_TYPE.faixaInfantil, PRODUCT_TYPE.outro],
};

function getProductConfig(productType, modality) {
  switch (productType) {
    case PRODUCT_TYPE.kimonoAdulto:
      return {
        sizes: ADULT_KIMONO_SIZES,
        sizeChart: ADULT_KIMONO_SIZE_CHART,
        colors: { label: 'Cor do kimono', values: KIMONO_COLORS },
        brands: ['In The Guard', 'South Team', 'Naja', 'Keiko'],
        bodyInfo: true,
        help: 'F2 e F3 são tamanhos femininos. A sugestão automática usa a tabela A0–A6.',
      };
    case PRODUCT_TYPE.kimonoInfantilJudo:
      return {
        sizes: INFANTIL_JUDO_SIZES,
        sizeChart: KIDS_KIMONO_SIZE_CHART,
        colors: { label: 'Cor do kimono', values: KIMONO_COLORS },
        bodyInfo: true,
        age: true,
        help: 'Também temos opções infantis para Judô.',
      };
    case PRODUCT_TYPE.faixaAdulto:
      return {
        sizes: ADULT_KIMONO_SIZES.slice(2),
        colors: { label: 'Cor da faixa', values: ADULT_BELT_COLORS, icon: getBeltColorIconSrc },
        brands: ['In The Guard', 'Venum', 'Naja'],
      };
    case PRODUCT_TYPE.faixaInfantil:
      return {
        sizes: INFANTIL_JUDO_SIZES,
        colors: {
          label: 'Cor da faixa',
          values: modality === MODALITY.judo ? JUDO_KIDS_BELT_COLORS : JIU_JITSU_KIDS_BELT_COLORS,
          icon: getBeltColorIconSrc,
        },
      };
    case PRODUCT_TYPE.rashguard:
      return {
        extras: [
          { field: FIELD.audience, label: 'Modelo', values: RASHGUARD_AUDIENCES },
          { field: FIELD.sleeve, label: 'Manga', values: SLEEVES },
        ],
        sizes: CLOTHING_SIZES,
        brands: ['In The Guard', 'Venum'],
      };
    case PRODUCT_TYPE.bermudaShorts:
      return { sizes: CLOTHING_SIZES, freeText: true };
    case PRODUCT_TYPE.luvas:
      return { sizes: GLOVE_SIZES, colors: { label: 'Cor da luva', values: GLOVE_COLORS } };
    case PRODUCT_TYPE.caneleira:
      return {
        sizes: SHIN_GUARD_SIZES,
        colors: { label: 'Cor da caneleira', values: SHIN_GUARD_COLORS },
        brands: ['South Team'],
      };
    default:
      return { freeText: true };
  }
}

const state = {
  step: 1,
  data: {},
  status: REQUEST_STATUS.idle,
  opener: null,
  successWhatsAppUrl: null,
  draftTimer: null,
  lastTrackedStep: null,
};

document.addEventListener('DOMContentLoaded', () => {
  state.data = loadDraft();
  injectRequestWidget();
  bindRequestEvents();
});

function injectRequestWidget() {
  document.body.insertAdjacentHTML('beforeend', `
    <a class="request-float" href="${buildWhatsAppUrl(WHATSAPP_FLOAT_MESSAGES[getPageCategory()] || WHATSAPP_FLOAT_MESSAGES.default)}" target="_blank" rel="noopener noreferrer" aria-label="Falar com a Morita no WhatsApp" data-track-event="whatsapp_catalog_click" data-track-category="request-widget">
      <i class="fab fa-whatsapp"></i>
      <span>Falar no WhatsApp</span>
    </a>
    <div class="request-modal" id="customer-request-modal" aria-hidden="true">
      <div class="request-backdrop" data-request-close></div>
      <section class="request-panel" role="dialog" aria-modal="true" aria-labelledby="request-title" tabindex="-1">
        <button class="request-close" type="button" aria-label="Fechar consulta" data-request-close>&times;</button>
        <p class="eyebrow">Guia rápido</p>
        <h2 id="request-title">Encontre seu tamanho e produto certo</h2>
        <p class="request-subtitle">Responda em poucos passos e a Morita confirma a melhor opção, disponibilidade e atendimento pelo WhatsApp.</p>
        <ol class="request-steps" aria-label="Etapas da consulta">
          ${STEPS.map((name, index) => `<li data-step="${index + 1}"><span class="request-step-number">${index + 1}</span>${name}</li>`).join('')}
        </ol>
        <p class="sr-only" aria-live="polite">Passo <span id="request-step">1</span> de ${TOTAL_STEPS}</p>
        <form id="customer-request-form" class="request-form" novalidate>
          <div id="request-step-content"></div>
          <div class="request-actions">
            <button class="request-secondary" type="button" data-request-back>Voltar</button>
            <button class="request-cancel" type="button" data-request-close>Cancelar</button>
            <button class="request-primary" type="button" data-request-next>${BUTTON_LABELS.continue}</button>
          </div>
        </form>
      </section>
    </div>
  `);
}

function bindRequestEvents() {
  document.addEventListener('click', (event) => {
    if (!(event.target instanceof Element)) return;

    const openButton = event.target.closest('[data-request-open]');
    if (!openButton) return;

    state.opener = openButton;
    openRequestModal(openButton.dataset.requestProduct || null, getPageCategory(), parsePrefill(openButton.dataset.requestPrefill));
  });

  document.querySelectorAll('[data-request-close]').forEach(button => {
    button.addEventListener('click', closeRequestModal);
  });

  const form = document.getElementById('customer-request-form');
  document.getElementById('customer-request-modal').addEventListener('keydown', handleModalKeydown);
  document.querySelector('[data-request-back]').addEventListener('click', goBack);
  document.querySelector('[data-request-next]').addEventListener('click', goNext);
  form.addEventListener('click', handleRequestFormClick);
  form.addEventListener('change', handleFormChange);
  form.addEventListener('input', handleFormInput);
  form.addEventListener('submit', event => event.preventDefault());
}

function parsePrefill(value) {
  if (!value) return null;
  try {
    const parsed = JSON.parse(value);
    return parsed && typeof parsed === 'object' ? parsed : null;
  } catch {
    return null;
  }
}

function openRequestModal(selectedProduct = null, pageCategory = null, prefill = null) {
  const modal = document.getElementById('customer-request-modal');
  modal.setAttribute('aria-hidden', 'false');
  document.body.classList.add('request-modal-open');
  state.step = 1;
  state.status = REQUEST_STATUS.idle;
  state.lastTrackedStep = null;
  document.querySelector('[data-request-next]').disabled = false;
  document.querySelector('.request-actions').style.display = 'flex';

  state.data[FIELD.productTypes] = getSelectedProductTypes();

  const mappedModality = selectedProduct ? findModalityForProduct(selectedProduct, pageCategory) : null;
  if (mappedModality) {
    const itemKey = `${mappedModality}${DETAIL_FIELD_SEPARATOR}${selectedProduct}`;
    if (!state.data[FIELD.productTypes].includes(itemKey) && state.data[FIELD.productTypes].length < MAX_ITEMS) {
      state.data[FIELD.productTypes].push(itemKey);
    }

    if (prefill) {
      applyPrefill(itemKey, prefill);
      // Coming from the size calculator the product and size are known, so go straight to the details.
      state.step = 2;
    }
  }

  renderStep();
  modal.querySelector('.request-panel').focus();
}

function applyPrefill(itemKey, prefill) {
  const allowedFields = [FIELD.size, FIELD.heightCm, FIELD.weightKg, FIELD.age, FIELD.color];
  const details = getItemDetails(itemKey);
  allowedFields.forEach(field => {
    if (prefill[field] !== undefined && prefill[field] !== null && prefill[field] !== '') {
      details[field] = String(prefill[field]);
    }
  });
  setItemDetails(itemKey, details);
}

function findModalityForProduct(productType, pageCategory) {
  const pageModality = getModalityForPage(pageCategory);

  if (pageModality && productTypesByModality[pageModality].includes(productType)) {
    return pageModality;
  }

  return Object.keys(productTypesByModality).find(modality => productTypesByModality[modality].includes(productType)) || null;
}

function getModalityForPage(pageCategory) {
  return {
    [PAGE_CATEGORY.jiuJitsu]: MODALITY.jiuJitsu,
    [PAGE_CATEGORY.muayThai]: MODALITY.muayThaiBoxe,
  }[pageCategory] || null;
}

function closeRequestModal() {
  if (state.status === REQUEST_STATUS.idle) {
    saveCurrentStep();
    saveDraft();
  }

  document.getElementById('customer-request-modal').setAttribute('aria-hidden', 'true');
  document.body.classList.remove('request-modal-open');
  state.opener?.focus();
  state.opener = null;
}

function handleModalKeydown(event) {
  if (event.key === 'Escape') {
    event.preventDefault();
    closeRequestModal();
    return;
  }

  if (event.key !== 'Tab') return;

  const focusable = [...document.querySelectorAll(`#customer-request-modal .request-panel :is(${FOCUSABLE_SELECTOR})`)]
    .filter(element => element.offsetParent !== null);
  if (focusable.length === 0) return;

  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  const panel = document.querySelector('#customer-request-modal .request-panel');

  if (event.shiftKey && (document.activeElement === first || document.activeElement === panel)) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

function handleRequestFormClick(event) {
  if (!(event.target instanceof Element)) return;

  if (event.target.closest('[data-request-add-products]')) {
    saveCurrentStep();
    state.step = 1;
    renderStep();
    return;
  }

  const addVariantButton = event.target.closest('[data-request-add-variant]');
  if (addVariantButton) {
    addVariant(addVariantButton.dataset.requestAddVariant);
    return;
  }

  const removeButton = event.target.closest('[data-request-remove-item]');
  if (removeButton) {
    removeItem(removeButton.dataset.requestRemoveItem);
    return;
  }

  const quantityButton = event.target.closest('[data-quantity-step]');
  if (quantityButton) {
    stepQuantity(quantityButton);
    return;
  }

  const applySizeButton = event.target.closest('[data-apply-size]');
  if (applySizeButton) {
    applySuggestedSize(applySizeButton.dataset.applySizeFor, applySizeButton.dataset.applySize);
  }
}

function addVariant(baseKey) {
  saveCurrentStep();
  const keys = getSelectedProductTypes();

  if (keys.length >= MAX_ITEMS) {
    showError(ERROR_MESSAGES.maxItems);
    return;
  }

  const lastIndexForBase = keys.map(getBaseKey).lastIndexOf(baseKey);
  const newKey = `${baseKey}${DETAIL_FIELD_SEPARATOR}${EXTRA_ITEM_PREFIX}${Date.now().toString(36)}`;
  keys.splice(lastIndexForBase + 1, 0, newKey);
  state.data[FIELD.productTypes] = keys;
  saveDraft();
  renderStep();

  const section = document.querySelector(`[data-item-key="${CSS.escape(newKey)}"]`);
  section?.scrollIntoView({ block: 'start', behavior: 'smooth' });
  section?.querySelector('input')?.focus({ preventScroll: true });
}

function removeItem(itemKey) {
  saveCurrentStep();
  state.data[FIELD.productTypes] = getSelectedProductTypes().filter(key => key !== itemKey);
  delete state.data[FIELD.productDetails]?.[itemKey];
  saveDraft();
  renderStep();
}

function stepQuantity(button) {
  const input = button.closest('.request-quantity')?.querySelector('input');
  if (!input) return;
  const next = clampQuantity(Number(input.value || 1) + Number(button.dataset.quantityStep));
  input.value = String(next);
  scheduleDraftSave();
}

function clampQuantity(value) {
  if (!Number.isFinite(value)) return 1;
  return Math.min(MAX_QUANTITY, Math.max(1, Math.round(value)));
}

function applySuggestedSize(itemKey, size) {
  const input = document.querySelector(`input[name="${CSS.escape(getDetailFieldName(FIELD.size, itemKey))}"][value="${CSS.escape(size)}"]`);
  if (!input) return;
  input.checked = true;
  syncChoiceSelection(input);
  input.closest('.request-choice')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  scheduleDraftSave();
}

function goBack() {
  if (state.step === 1) return;
  saveCurrentStep();
  state.step -= 1;
  renderStep();
}

async function goNext() {
  saveCurrentStep();
  saveDraft();

  if (!isCurrentStepValid()) return;

  if (state.step < TOTAL_STEPS) {
    state.step += 1;
    renderStep();
    document.querySelector('#customer-request-modal .request-panel').scrollTop = 0;
    return;
  }

  await submitRequest();
}

function saveCurrentStep() {
  if (state.status !== REQUEST_STATUS.idle) return;

  const form = document.getElementById('customer-request-form');
  const formData = new FormData(form);

  if (state.step === 1) {
    const selectedBases = formData.getAll(FIELD.productTypes).map(value => value.toString());
    state.data[FIELD.productTypes] = mergeSelectedBases(getSelectedProductTypes(), selectedBases);

    const selectedKeys = state.data[FIELD.productTypes];
    state.data[FIELD.productDetails] = Object.fromEntries(
      Object.entries(state.data[FIELD.productDetails] || {}).filter(([key]) => selectedKeys.includes(key))
    );
    return;
  }

  if (state.step === 2) {
    const details = {};
    formData.forEach((value, key) => {
      const parts = key.split(DETAIL_FIELD_SEPARATOR);
      if (parts.length < 3) return;
      const field = parts[0];
      const itemKey = parts.slice(1).join(DETAIL_FIELD_SEPARATOR);

      details[itemKey] = details[itemKey] || {};
      details[itemKey][field] = value.toString();
    });
    state.data[FIELD.productDetails] = details;
    return;
  }

  [FIELD.customerName, FIELD.customerPhone, FIELD.deliveryMethod, FIELD.deliveryArea, FIELD.deadline, FIELD.notes, FIELD.website].forEach(field => {
    const value = formData.get(field);
    if (value !== null) state.data[field] = value.toString();
  });

  state.data[FIELD.notifyRestock] = formData.get(FIELD.notifyRestock) === 'true';

  state.data[FIELD.acceptedPrivacyPolicy] = formData.get(FIELD.acceptedPrivacyPolicy) === 'true';
}

// Keeps the existing items (including extra sizes) for bases that are still
// selected, in their current order, and appends newly selected bases.
function mergeSelectedBases(currentKeys, selectedBases) {
  const kept = currentKeys.filter(key => selectedBases.includes(getBaseKey(key)));
  const keptBases = new Set(kept.map(getBaseKey));
  const added = selectedBases.filter(base => !keptBases.has(base));
  return [...kept, ...added];
}

function isCurrentStepValid() {
  const form = document.getElementById('customer-request-form');
  const formData = new FormData(form);

  if (state.step === 1) {
    const selected = formData.getAll(FIELD.productTypes);
    if (selected.length === 0) {
      showError(ERROR_MESSAGES.products);
      return false;
    }
    if (getSelectedProductTypes().length > MAX_ITEMS) {
      showError(ERROR_MESSAGES.maxItems);
      return false;
    }
    return true;
  }

  if (state.step === 3) {
    const name = formData.get(FIELD.customerName)?.toString().trim();
    const phone = formData.get(FIELD.customerPhone)?.toString().trim();

    if (!name || !phone) {
      showError(ERROR_MESSAGES.required);
      return false;
    }

    if (!isValidPhone(phone)) {
      showError(ERROR_MESSAGES.phone);
      document.querySelector(`input[name="${FIELD.customerPhone}"]`)?.focus();
      return false;
    }
  }

  return true;
}

function showError(message) {
  document.querySelector('.request-error')?.remove();
  document.getElementById('request-step-content').insertAdjacentHTML('beforeend', renderError(message));
}

function renderStep() {
  document.getElementById('request-step').textContent = state.step;
  document.querySelectorAll('.request-steps li').forEach(item => {
    const step = Number(item.dataset.step);
    item.classList.toggle('is-active', step === state.step && state.status === REQUEST_STATUS.idle);
    item.classList.toggle('is-done', step < state.step || state.status === REQUEST_STATUS.success);
    if (step === state.step) item.setAttribute('aria-current', 'step');
    else item.removeAttribute('aria-current');
  });
  document.querySelector('[data-request-back]').style.visibility = state.step === 1 ? 'hidden' : 'visible';
  document.querySelector('[data-request-next]').textContent = state.step === TOTAL_STEPS ? BUTTON_LABELS.submit : BUTTON_LABELS.continue;

  const content = document.getElementById('request-step-content');
  content.innerHTML = getStepHtml();

  // Re-renders within a step (adding a size, removing an item) are not new step views.
  if (state.status === REQUEST_STATUS.idle && state.lastTrackedStep !== state.step) {
    state.lastTrackedStep = state.step;
    document.dispatchEvent(new CustomEvent('morita:request-step', {
      detail: { step: state.step, stepName: STEPS[state.step - 1] },
    }));
  }
}

function handleFormChange(event) {
  const target = event.target;
  if (!(target instanceof HTMLInputElement)) return;

  if (target.type === 'radio' || target.type === 'checkbox') {
    syncChoiceSelection(target);
  }

  if (target.name === FIELD.acceptedPrivacyPolicy) {
    state.data[FIELD.acceptedPrivacyPolicy] = target.checked;
    document.querySelector('.request-error')?.remove();
    return;
  }

  if (target.name === FIELD.deliveryMethod) {
    const areaField = document.querySelector('[data-delivery-area]');
    if (areaField) areaField.hidden = target.value !== DELIVERY_METHOD.delivery;
  }

  if (target.name === FIELD.productTypes) {
    document.querySelector('.request-error')?.remove();
  }

  scheduleDraftSave();
}

function handleFormInput(event) {
  const target = event.target;
  if (!(target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement)) return;

  if (target.name === FIELD.customerPhone) {
    const caretAtEnd = target.selectionStart === target.value.length;
    target.value = formatPhone(target.value);
    if (caretAtEnd) target.setSelectionRange(target.value.length, target.value.length);
  }

  const [field, ...keyParts] = target.name.split(DETAIL_FIELD_SEPARATOR);
  if ((field === FIELD.heightCm || field === FIELD.weightKg) && keyParts.length >= 2) {
    updateSizeSuggestion(keyParts.join(DETAIL_FIELD_SEPARATOR));
  }

  scheduleDraftSave();
}

// Radio/checkbox cards carry a `selected` class for styling; keep it in sync
// with the real input state after user changes.
function syncChoiceSelection(input) {
  const group = input.type === 'radio'
    ? document.querySelectorAll(`input[name="${CSS.escape(input.name)}"]`)
    : [input];
  group.forEach(element => element.closest('.request-choice')?.classList.toggle('selected', element.checked));
}

function getStepHtml() {
  if (state.status === REQUEST_STATUS.success) {
    return renderSuccess();
  }

  if (state.step === 1) {
    return renderProductSelectionStep();
  }

  if (state.step === 2) {
    return renderProductQuestions();
  }

  return renderReviewStep();
}

function renderProductSelectionStep() {
  const selectedBases = new Set(getSelectedProductTypes().map(getBaseKey));
  const pageModality = getModalityForPage(getPageCategory());
  const groups = Object.entries(productTypesByModality);
  const primaryGroups = pageModality ? groups.filter(([modality]) => modality === pageModality) : groups;
  const otherGroups = pageModality ? groups.filter(([modality]) => modality !== pageModality) : [];
  const hasOtherSelection = otherGroups.some(([modality, types]) => types.some(type => selectedBases.has(buildBaseKey(modality, type))));

  const renderGroup = ([modality, types]) => `
    <div class="request-modality-group">
      <h4 class="request-group-title">${modality}</h4>
      <div class="request-choice-grid">
        ${types.map(type => {
          const itemKey = buildBaseKey(modality, type);
          const isChecked = selectedBases.has(itemKey);
          return `
            <label class="request-choice request-choice-multiple ${isChecked ? 'selected' : ''}">
              <input type="checkbox" name="${FIELD.productTypes}" value="${escapeHtml(itemKey)}" ${isChecked ? 'checked' : ''}>
              <span>${type}</span>
            </label>
          `;
        }).join('')}
      </div>
    </div>
  `;

  return `
    <fieldset class="request-choice-group">
      <legend>O que você quer encontrar?</legend>
      <p class="request-help">Selecione um ou mais itens. Na próxima etapa você escolhe tamanho, cor e quantidade.</p>
      ${primaryGroups.map(renderGroup).join('')}
      ${otherGroups.length > 0 ? `
        <details class="request-more-modalities" ${hasOtherSelection ? 'open' : ''}>
          <summary>Ver outras modalidades (${otherGroups.map(([modality]) => modality).join(', ')})</summary>
          ${otherGroups.map(renderGroup).join('')}
        </details>
      ` : ''}
    </fieldset>
  `;
}

function renderProductQuestions() {
  const selectedKeys = getSelectedProductTypes();

  if (selectedKeys.length === 0) {
    return '<p class="request-help">Selecione pelo menos um produto no passo anterior.</p>';
  }

  const canAddMore = selectedKeys.length < MAX_ITEMS;

  return selectedKeys.map((key, index) => {
    const base = getBaseKey(key);
    const isLastOfBase = selectedKeys.slice(index + 1).every(other => getBaseKey(other) !== base);
    return renderProductQuestionGroup(key, { canAddMore, isLastOfBase });
  }).join('');
}

function renderProductQuestionGroup(key, { canAddMore, isLastOfBase }) {
  const { modality, productType } = parseItemKey(key);
  const config = getProductConfig(productType, modality);
  const details = getItemDetails(key);
  const variantNumber = getVariantNumber(key);
  const isExtra = variantNumber > 1;

  return `
    <section class="request-product-section" data-item-key="${escapeHtml(key)}">
      <div class="request-product-head">
        <h3>${productType}${isExtra ? ` <span class="request-variant">#${variantNumber}</span>` : ''} <span class="request-badge">${modality}</span></h3>
        ${isExtra ? `<button class="request-remove" type="button" data-request-remove-item="${escapeHtml(key)}" aria-label="Remover ${productType} #${variantNumber}">Remover</button>` : ''}
      </div>
      ${config.extras ? config.extras.map(extra => renderDetailChoiceGroup(extra.field, key, extra.label, extra.values, details[extra.field])).join('') : ''}
      ${config.bodyInfo ? renderBodyInfo(key, config, details) : ''}
      ${config.sizes ? renderDetailChoiceGroup(FIELD.size, key, 'Tamanho', [...config.sizes, UNKNOWN_SIZE], details[FIELD.size]) : ''}
      ${config.colors ? renderDetailChoiceGroup(FIELD.color, key, config.colors.label, config.colors.values, details[FIELD.color], config.colors.icon) : ''}
      ${config.brands ? renderDetailChoiceGroup(FIELD.brand, key, 'Marca (opcional)', [NO_BRAND_PREFERENCE, ...config.brands], details[FIELD.brand]) : ''}
      ${config.freeText ? renderProductDetailsField(key, details[FIELD.productDetails]) : ''}
      ${config.help ? `<p class="request-help">${config.help}</p>` : ''}
      ${renderQuantityField(key, details[FIELD.quantity])}
      ${isLastOfBase && canAddMore && config.sizes ? `
        <button class="request-add-variant" type="button" data-request-add-variant="${escapeHtml(getBaseKey(key))}">
          + Adicionar outro tamanho ou cor de ${productType}
        </button>
      ` : ''}
    </section>
  `;
}

function renderBodyInfo(key, config, details) {
  const suggestion = config.sizeChart ? suggestKimonoSize(config.sizeChart, details[FIELD.heightCm], details[FIELD.weightKg]) : null;

  return `
    <div class="request-body-info">
      <p class="request-help">Informe altura e peso para receber uma sugestão de tamanho.</p>
      <div class="request-inline-fields">
        ${renderNumberField(FIELD.heightCm, key, 'Altura (cm)', details[FIELD.heightCm], { min: 50, max: 230, step: 1 })}
        ${renderNumberField(FIELD.weightKg, key, 'Peso (kg)', details[FIELD.weightKg], { min: 10, max: 250, step: 0.1 })}
        ${config.age ? renderNumberField(FIELD.age, key, 'Idade', details[FIELD.age], { min: 1, max: 120, step: 1 }) : ''}
      </div>
      <div class="request-size-suggestion" data-suggestion-for="${escapeHtml(key)}" aria-live="polite">
        ${renderSizeSuggestion(key, suggestion)}
      </div>
    </div>
  `;
}

function renderNumberField(field, key, label, value, { min, max, step }) {
  return `
    <label class="request-label">${label}
      <input name="${escapeHtml(getDetailFieldName(field, key))}" type="number" inputmode="decimal" min="${min}" max="${max}" step="${step}" value="${escapeHtml(value || '')}">
    </label>
  `;
}

function renderSizeSuggestion(key, size) {
  if (!size) return '';
  return `
    <span>Tamanho sugerido: <strong>${size}</strong></span>
    <button type="button" class="request-inline-action" data-apply-size="${size}" data-apply-size-for="${escapeHtml(key)}">Usar ${size}</button>
    <small>Sugestão aproximada: a Morita confirma o tamanho ideal com você.</small>
  `;
}

function updateSizeSuggestion(key) {
  const { modality, productType } = parseItemKey(key);
  const config = getProductConfig(productType, modality);
  if (!config.sizeChart) return;

  const height = document.querySelector(`input[name="${CSS.escape(getDetailFieldName(FIELD.heightCm, key))}"]`)?.value;
  const weight = document.querySelector(`input[name="${CSS.escape(getDetailFieldName(FIELD.weightKg, key))}"]`)?.value;
  const container = document.querySelector(`[data-suggestion-for="${CSS.escape(key)}"]`);
  if (container) container.innerHTML = renderSizeSuggestion(key, suggestKimonoSize(config.sizeChart, height, weight));
}

function renderQuantityField(key, value) {
  const quantity = clampQuantity(Number(value || 1));
  const name = getDetailFieldName(FIELD.quantity, key);
  return `
    <div class="request-quantity">
      <span class="request-quantity-label">Quantidade</span>
      <button type="button" data-quantity-step="-1" aria-label="Diminuir quantidade">−</button>
      <input name="${escapeHtml(name)}" type="number" inputmode="numeric" min="1" max="${MAX_QUANTITY}" value="${quantity}" aria-label="Quantidade">
      <button type="button" data-quantity-step="1" aria-label="Aumentar quantidade">+</button>
    </div>
  `;
}

function getBeltColorIconSrc(value) {
  const filename = value.toLowerCase().replace(/\s+e\s+/g, '-e-').replace(/\s+/g, '-');
  return `${BELT_ICON_BASE_PATH}${filename}.png`;
}

// Short values (sizes, colors, brands) render as compact chips; belt colors keep icon cards.
function renderDetailChoiceGroup(field, key, label, values, selectedValue, getIconSrc = null) {
  const layout = getIconSrc ? CHOICE_LAYOUT.cards : CHOICE_LAYOUT.chips;
  return renderChoiceGroup(getDetailFieldName(field, key), label, values, selectedValue, getIconSrc, layout);
}

function renderChoiceGroup(name, label, values, selectedValue = null, getIconSrc = null, layout = CHOICE_LAYOUT.cards) {
  return `
    <fieldset class="request-choice-group">
      <legend>${label}</legend>
      <div class="request-choice-grid ${layout}">
        ${values.map(value => {
          const isSelected = selectedValue === value;
          return `
            <label class="request-choice ${isSelected ? 'selected' : ''}">
              <input type="radio" name="${escapeHtml(name)}" value="${escapeHtml(value)}" ${isSelected ? 'checked' : ''}>
              <span>${getIconSrc ? `<img class="request-choice-icon" src="${getIconSrc(value)}" alt="" aria-hidden="true">` : ''}${value}</span>
            </label>
          `;
        }).join('')}
      </div>
    </fieldset>
  `;
}

function renderProductDetailsField(key, value) {
  const name = getDetailFieldName(FIELD.productDetails, key);
  return `
    <label class="request-label">
      Detalhes do produto (marca, modelo, cor preferida)
      <textarea name="${escapeHtml(name)}" rows="2" maxlength="${DETAILS_MAX_LENGTH}">${escapeHtml(value || '')}</textarea>
    </label>
  `;
}

function renderReviewStep() {
  const deliveryMethod = state.data[FIELD.deliveryMethod] || '';

  return `
    ${renderSelectedProductsSummary()}
    <label class="request-label">Seu nome<input name="${FIELD.customerName}" value="${escapeHtml(state.data[FIELD.customerName] || '')}" autocomplete="name" maxlength="120" required></label>
    <label class="request-label">WhatsApp<input name="${FIELD.customerPhone}" value="${escapeHtml(formatPhone(state.data[FIELD.customerPhone] || ''))}" type="tel" inputmode="tel" autocomplete="tel-national" placeholder="(15) 99999-9999" maxlength="16" required></label>
    ${renderChoiceGroup(FIELD.deliveryMethod, 'Como prefere receber?', Object.values(DELIVERY_METHOD), deliveryMethod, null, CHOICE_LAYOUT.compact)}
    <label class="request-label" data-delivery-area ${deliveryMethod === DELIVERY_METHOD.delivery ? '' : 'hidden'}>Bairro e cidade<input name="${FIELD.deliveryArea}" value="${escapeHtml(state.data[FIELD.deliveryArea] || '')}" autocomplete="address-level3" maxlength="120" placeholder="Ex.: Centro, Sorocaba"></label>
    ${renderChoiceGroup(FIELD.deadline, 'Para quando você precisa?', DEADLINES, state.data[FIELD.deadline] || '', null, CHOICE_LAYOUT.compact)}
    <label class="request-checkbox request-notify">
      <input type="checkbox" name="${FIELD.notifyRestock}" value="true" ${state.data[FIELD.notifyRestock] === true ? 'checked' : ''}>
      <span>Se algum item estiver em falta, me avise quando chegar.</span>
    </label>
    <label class="request-label">Observações (opcional)<textarea name="${FIELD.notes}" rows="2" maxlength="${NOTES_MAX_LENGTH}">${escapeHtml(state.data[FIELD.notes] || '')}</textarea></label>
    ${renderPrivacyConsent()}
    <input class="request-honeypot" name="${FIELD.website}" tabindex="-1" autocomplete="off" aria-hidden="true">
  `;
}

function renderSelectedProductsSummary() {
  const selectedKeys = getSelectedProductTypes();

  return `
    <section class="request-selection-summary" aria-label="Produtos selecionados">
      <div class="request-selection-summary-head">
        <h3>Produtos selecionados</h3>
        <button class="request-inline-action" type="button" data-request-add-products>Adicionar/Alterar produtos</button>
      </div>
      ${selectedKeys.length > 0 ? selectedKeys.map(renderSelectedProductSummaryItem).join('') : '<p class="request-help">Nenhum produto selecionado.</p>'}
    </section>
  `;
}

function renderSelectedProductSummaryItem(key) {
  const { modality } = parseItemKey(key);
  const summary = getItemSummary(key);

  return `
    <article class="request-selection-item">
      <strong>${getItemDisplayName(key)} <span class="request-badge">${modality}</span></strong>
      ${summary.length > 0 ? `<dl>${summary.map(([label, value]) => `<div><dt>${label}</dt><dd>${escapeHtml(value)}</dd></div>`).join('')}</dl>` : '<span>Sem detalhes adicionais</span>'}
    </article>
  `;
}

function getItemSummary(key) {
  const details = getItemDetails(key);
  const quantity = clampQuantity(Number(details[FIELD.quantity] || 1));
  return [
    ['Qtd.', quantity > 1 ? String(quantity) : ''],
    ['Modelo', details[FIELD.audience]],
    ['Manga', details[FIELD.sleeve]],
    ['Tamanho', details[FIELD.size]],
    ['Cor', details[FIELD.color]],
    ['Marca', details[FIELD.brand]],
    ['Altura', details[FIELD.heightCm] ? `${details[FIELD.heightCm]} cm` : ''],
    ['Peso', details[FIELD.weightKg] ? `${details[FIELD.weightKg]} kg` : ''],
    ['Idade', details[FIELD.age]],
    ['Detalhes', details[FIELD.productDetails]?.trim()],
  ].filter(([, value]) => value);
}

function renderPrivacyConsent() {
  return `
    <section class="request-privacy" aria-label="Política de Privacidade">
      <label class="request-checkbox">
        <input type="checkbox" name="${FIELD.acceptedPrivacyPolicy}" value="true" ${state.data[FIELD.acceptedPrivacyPolicy] === true ? 'checked' : ''}>
        <span>Estou de acordo em compartilhar esses dados para que a Morita entre em contato comigo para prosseguir com o atendimento. <a href="/Privacidade" target="_blank" rel="noopener">Política de Privacidade</a></span>
      </label>
    </section>
  `;
}

async function submitRequest() {
  const button = document.querySelector('[data-request-next]');
  if (state.data[FIELD.acceptedPrivacyPolicy] !== true) {
    showError(ERROR_MESSAGES.privacy);
    return;
  }

  const whatsAppUrl = buildWhatsAppUrl(buildRequestWhatsAppMessage());
  button.disabled = true;
  button.textContent = BUTTON_LABELS.submitting;

  const payload = buildPayload();

  try {
    const response = await fetch(`${API_BASE_URL}/v1/CustomerProductRequest`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });

    if (!response.ok) throw new Error(`Request failed with status ${response.status}`);

    document.dispatchEvent(new CustomEvent('morita:lead', {
      detail: {
        modality: payload.modality,
        itemCount: payload.items.length,
        productTypes: [...new Set(payload.items.map(item => item.productType))].join(', '),
      },
    }));

    state.status = REQUEST_STATUS.success;
    state.successWhatsAppUrl = whatsAppUrl;
    keepOnlyContactInDraft();
    document.querySelector('.request-actions').style.display = 'none';
    renderStep();
  } catch {
    document.dispatchEvent(new CustomEvent('morita:request-error', {
      detail: { modality: payload.modality, itemCount: payload.items.length },
    }));
    document.querySelector('.request-error')?.remove();
    document.getElementById('request-step-content').insertAdjacentHTML('beforeend', renderSubmitError(whatsAppUrl));
    button.disabled = false;
    button.textContent = BUTTON_LABELS.submit;
  }
}

function renderError(message) {
  return `<p class="request-error" role="alert">${message}</p>`;
}

function renderSubmitError(whatsAppUrl) {
  return `
    <div class="request-error" role="alert">
      <p>${ERROR_MESSAGES.submit}</p>
      ${renderWhatsAppButton(whatsAppUrl, 'Enviar pelo WhatsApp', 'whatsapp_request_fallback_click')}
    </div>
  `;
}

function renderSuccess() {
  return `
    <div class="request-success">
      <strong>Pedido recebido!</strong>
      <span>Vamos confirmar tamanho e disponibilidade pelo WhatsApp. Quer agilizar? Envie o resumo agora e fale direto com a nossa equipe.</span>
      ${renderWhatsAppButton(state.successWhatsAppUrl, 'Abrir conversa no WhatsApp', 'whatsapp_request_success_click')}
    </div>
  `;
}

function renderWhatsAppButton(url, label, trackEvent) {
  return `
    <a class="request-whatsapp" href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer" data-track-event="${trackEvent}" data-track-category="request-widget">
      <i class="fab fa-whatsapp"></i>
      ${label}
    </a>
  `;
}

function buildRequestWhatsAppMessage() {
  const lines = ['Olá, Morita! Fiz uma consulta pelo site:'];
  const name = state.data[FIELD.customerName]?.trim();
  if (name) lines.push(`Nome: ${name}`);

  getSelectedProductTypes().forEach(key => {
    const { modality } = parseItemKey(key);
    const details = getItemSummary(key).map(([label, value]) => `${label}: ${value}`).join(', ');
    lines.push(`- ${getItemDisplayName(key)} (${modality})${details ? ` - ${details}` : ''}`);
  });

  getRequestNoteParts().forEach(part => lines.push(part));

  return lines.join('\n');
}

function buildWhatsAppUrl(message) {
  return `https://wa.me/${WHATSAPP_PHONE}?text=${encodeURIComponent(message)}`;
}

function getPageCategory() {
  if (document.body.classList.contains('jiu-jitsu-page')) return PAGE_CATEGORY.jiuJitsu;
  if (document.body.classList.contains('muay-thai-page')) return PAGE_CATEGORY.muayThai;
  return null;
}

function buildPayload() {
  const selectedKeys = getSelectedProductTypes();
  return {
    customerName: state.data[FIELD.customerName]?.trim(),
    customerPhone: formatPhone(state.data[FIELD.customerPhone] || ''),
    modality: getSelectedModalities().join(', ') || 'Outro',
    notes: buildRequestNotes(),
    website: state.data[FIELD.website] || null,
    acceptedPrivacyPolicy: true,
    source: 'landing-page',
    landingPage: getLandingPagePath(),
    campaign: getCampaign(),
    items: selectedKeys.map(key => {
      const { modality, productType } = parseItemKey(key);
      const details = getItemDetails(key);
      const brand = details[FIELD.brand];

      return {
        modality,
        productType,
        brandPreference: !brand
          ? BRAND_PREFERENCE.notInformed
          : brand === NO_BRAND_PREFERENCE ? BRAND_PREFERENCE.noPreference : BRAND_PREFERENCE.specific,
        brand: brand && brand !== NO_BRAND_PREFERENCE ? brand : null,
        size: details[FIELD.size] || null,
        color: details[FIELD.color] || null,
        heightCm: toNumberOrNull(details[FIELD.heightCm], true),
        weightKg: toNumberOrNull(details[FIELD.weightKg]),
        age: toNumberOrNull(details[FIELD.age], true),
        notes: buildItemNotes(details),
      };
    }),
  };
}

function toNumberOrNull(value, integer = false) {
  if (value === undefined || value === null || value === '') return null;
  const number = Number(String(value).replace(',', '.'));
  if (!Number.isFinite(number)) return null;
  return integer ? Math.round(number) : number;
}

function getLandingPagePath() {
  const path = window.location.pathname.replace(/\/$/, '');
  return path || '/';
}

function getCampaign() {
  const params = new URLSearchParams(window.location.search);
  return params.get('campaign') || params.get('utm_campaign') || null;
}

function buildItemNotes(details) {
  const quantity = clampQuantity(Number(details[FIELD.quantity] || 1));
  const parts = [
    quantity > 1 ? `Quantidade: ${quantity}` : '',
    details[FIELD.audience] ? `Modelo: ${details[FIELD.audience]}` : '',
    details[FIELD.sleeve] ? `Manga: ${details[FIELD.sleeve]}` : '',
    details[FIELD.productDetails]?.trim() ? `Detalhes do produto: ${details[FIELD.productDetails].trim()}` : '',
  ].filter(Boolean);

  return parts.length > 0 ? parts.join(' | ') : null;
}

function getRequestNoteParts() {
  const deliveryMethod = state.data[FIELD.deliveryMethod];
  const deliveryArea = state.data[FIELD.deliveryArea]?.trim();
  const notes = state.data[FIELD.notes]?.trim();

  return [
    deliveryMethod ? `Entrega: ${deliveryMethod}${deliveryMethod === DELIVERY_METHOD.delivery && deliveryArea ? ` (${deliveryArea})` : ''}` : '',
    state.data[FIELD.deadline] ? `Prazo: ${state.data[FIELD.deadline]}` : '',
    state.data[FIELD.notifyRestock] === true ? 'Avisar quando chegar: sim' : '',
    notes ? `Observações: ${notes}` : '',
  ].filter(Boolean);
}

function buildRequestNotes() {
  const parts = getRequestNoteParts();
  return parts.length > 0 ? parts.join('\n') : null;
}

function getSelectedProductTypes() {
  return Array.isArray(state.data[FIELD.productTypes]) ? [...state.data[FIELD.productTypes]] : [];
}

function getSelectedModalities() {
  const modalities = getSelectedProductTypes().map(key => parseItemKey(key).modality);
  return [...new Set(modalities)];
}

function getDetailFieldName(field, key) {
  return `${field}${DETAIL_FIELD_SEPARATOR}${key}`;
}

function buildBaseKey(modality, productType) {
  return `${modality}${DETAIL_FIELD_SEPARATOR}${productType}`;
}

function parseItemKey(key) {
  const [modality, productType] = key.split(DETAIL_FIELD_SEPARATOR);
  return { modality, productType };
}

function getBaseKey(key) {
  const { modality, productType } = parseItemKey(key);
  return buildBaseKey(modality, productType);
}

function getVariantNumber(key) {
  const base = getBaseKey(key);
  return getSelectedProductTypes().filter(other => getBaseKey(other) === base).indexOf(key) + 1;
}

function getItemDisplayName(key) {
  const { productType } = parseItemKey(key);
  const variantNumber = getVariantNumber(key);
  return variantNumber > 1 ? `${productType} #${variantNumber}` : productType;
}

function getItemDetails(key) {
  return { ...(state.data[FIELD.productDetails]?.[key] || {}) };
}

function setItemDetails(key, details) {
  state.data[FIELD.productDetails] = { ...(state.data[FIELD.productDetails] || {}), [key]: details };
}

function formatPhone(value) {
  let digits = String(value).replace(/\D/g, '');
  if (digits.startsWith('55') && digits.length > 11) digits = digits.slice(2);
  digits = digits.slice(0, 11);

  if (digits.length <= 2) return digits ? `(${digits}` : '';
  if (digits.length <= 6) return `(${digits.slice(0, 2)}) ${digits.slice(2)}`;
  if (digits.length <= 10) return `(${digits.slice(0, 2)}) ${digits.slice(2, 6)}-${digits.slice(6)}`;
  return `(${digits.slice(0, 2)}) ${digits.slice(2, 7)}-${digits.slice(7)}`;
}

function isValidPhone(value) {
  const digits = formatPhone(value).replace(/\D/g, '');
  return digits.length === 10 || digits.length === 11;
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

// Draft persistence: lets a visitor close the modal or reload the page without
// losing what they typed. Stored only in this browser; consent is never kept.
function scheduleDraftSave() {
  clearTimeout(state.draftTimer);
  state.draftTimer = setTimeout(() => {
    saveCurrentStep();
    saveDraft();
  }, DRAFT_SAVE_DELAY_MS);
}

function saveDraft() {
  if (state.status !== REQUEST_STATUS.idle) return;
  const { [FIELD.acceptedPrivacyPolicy]: _consent, [FIELD.website]: _honeypot, ...draft } = state.data;
  writeStorage(draft);
}

function keepOnlyContactInDraft() {
  state.data = {
    [FIELD.customerName]: state.data[FIELD.customerName],
    [FIELD.customerPhone]: state.data[FIELD.customerPhone],
  };
  writeStorage(state.data);
}

function loadDraft() {
  try {
    const raw = window.localStorage.getItem(DRAFT_STORAGE_KEY);
    const draft = raw ? JSON.parse(raw) : null;
    if (!draft || typeof draft !== 'object') return {};

    const knownBases = new Set(Object.entries(productTypesByModality).flatMap(([modality, types]) => types.map(type => buildBaseKey(modality, type))));
    const keys = Array.isArray(draft[FIELD.productTypes])
      ? draft[FIELD.productTypes].filter(key => typeof key === 'string' && knownBases.has(getBaseKey(key))).slice(0, MAX_ITEMS)
      : [];

    return { ...draft, [FIELD.productTypes]: keys };
  } catch {
    return {};
  }
}

function writeStorage(value) {
  try {
    window.localStorage.setItem(DRAFT_STORAGE_KEY, JSON.stringify(value));
  } catch {
    // Storage can be unavailable (private mode, blocked site data); the form still works.
  }
}
