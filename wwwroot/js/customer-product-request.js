const API_BASE_URL = window.API_BASE_URL || (window.location.hostname === 'localhost'
  ? 'http://localhost:5001'
  : 'https://morita-api-1nnj.onrender.com');

const WHATSAPP_NUMBER = '5515981079332';
const TOTAL_STEPS = 3;

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
  required: 'Preencha as informações obrigatórias para continuar.',
  privacy: 'Confirme o uso dos seus dados para enviar a consulta.',
  submit: 'Não foi possível enviar agora. Tente pelo WhatsApp.',
};

const FIELD = {
  modality: 'modality',
  productTypes: 'productTypes',
  productDetails: 'productDetails',
  size: 'size',
  color: 'color',
  heightCm: 'heightCm',
  weightKg: 'weightKg',
  age: 'age',
  customerName: 'customerName',
  customerPhone: 'customerPhone',
  notes: 'notes',
  website: 'website',
  acceptedPrivacyPolicy: 'acceptedPrivacyPolicy',
};

const DETAIL_FIELD_SEPARATOR = '::';
const PRODUCT_DETAILS_MAX_LENGTH = 500;

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

const SIZE = {
  naoSei: 'Não sei',
};

const ADULT_KIMONO_SIZES = ['F2', 'F3', 'A0', 'A1', 'A2', 'A3', 'A4', 'A5', 'A6'];
const INFANTIL_JUDO_SIZES = ['M000', 'M00', 'M0', 'M1', 'M2', 'M3', 'M4'];
const CLOTHING_SIZES = ['PP', 'P', 'M', 'G', 'GG', 'XGG'];
const UNKNOWN_SIZE_OPTION = [SIZE.naoSei];
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

const options = {
  modalities: [MODALITY.jiuJitsu, MODALITY.muayThaiBoxe, MODALITY.mma, MODALITY.judo],
  productTypes: {
    [MODALITY.jiuJitsu]: [PRODUCT_TYPE.kimonoAdulto, PRODUCT_TYPE.kimonoInfantilJudo, PRODUCT_TYPE.faixaAdulto, PRODUCT_TYPE.faixaInfantil, PRODUCT_TYPE.rashguard, PRODUCT_TYPE.bermudaShorts],
    [MODALITY.muayThaiBoxe]: [PRODUCT_TYPE.luvas, PRODUCT_TYPE.protetorBucal, PRODUCT_TYPE.caneleira, PRODUCT_TYPE.bermudaShorts, PRODUCT_TYPE.outro],
    [MODALITY.mma]: [PRODUCT_TYPE.bermudaShorts, PRODUCT_TYPE.rashguard, PRODUCT_TYPE.luvas, PRODUCT_TYPE.outro],
    [MODALITY.judo]: [PRODUCT_TYPE.kimonoInfantilJudo, PRODUCT_TYPE.faixaAdulto, PRODUCT_TYPE.faixaInfantil, PRODUCT_TYPE.outro],
  },
  sizes: {
    [PRODUCT_TYPE.kimonoAdulto]: [...ADULT_KIMONO_SIZES, ...UNKNOWN_SIZE_OPTION],
    [PRODUCT_TYPE.kimonoInfantilJudo]: [...INFANTIL_JUDO_SIZES, ...UNKNOWN_SIZE_OPTION],
    [PRODUCT_TYPE.faixaAdulto]: [...ADULT_KIMONO_SIZES.slice(2), ...UNKNOWN_SIZE_OPTION],
    [PRODUCT_TYPE.faixaInfantil]: [...INFANTIL_JUDO_SIZES, ...UNKNOWN_SIZE_OPTION],
    [PRODUCT_TYPE.rashguard]: [...CLOTHING_SIZES, ...UNKNOWN_SIZE_OPTION],
    [PRODUCT_TYPE.bermudaShorts]: [...CLOTHING_SIZES, ...UNKNOWN_SIZE_OPTION],
    [PRODUCT_TYPE.luvas]: ['8oz', '10oz', '12oz', '14oz', '16oz', ...UNKNOWN_SIZE_OPTION],
  },
};

const state = {
  step: 1,
  data: {},
  status: REQUEST_STATUS.idle,
};

document.addEventListener('DOMContentLoaded', () => {
  injectRequestWidget();
  bindRequestEvents();
});

function injectRequestWidget() {
  document.body.insertAdjacentHTML('beforeend', `
    <a class="request-float" href="https://wa.me/c/5515981079332" target="_blank" rel="noopener noreferrer" aria-label="Falar com a Morita Fitness no WhatsApp" data-track-event="whatsapp_catalog_click" data-track-category="request-widget">
      <i class="fas fa-phone"></i>
      Falar no WhatsApp
    </a>
    <div class="request-modal" id="customer-request-modal" aria-hidden="true">
      <div class="request-backdrop" data-request-close></div>
      <section class="request-panel" role="dialog" aria-modal="true" aria-labelledby="request-title">
        <button class="request-close" type="button" aria-label="Fechar consulta" data-request-close>&times;</button>
        <p class="eyebrow">Guia rápido</p>
        <h2 id="request-title">Encontre seu tamanho e produto certo</h2>
        <p class="request-subtitle">Responda em poucos passos e a Morita confirma a melhor opção, disponibilidade e atendimento pelo WhatsApp.</p>
        <div class="request-progress">Passo <span id="request-step">1</span> de ${TOTAL_STEPS}</div>
        <form id="customer-request-form" class="request-form">
          <div id="request-step-content"></div>
          <div class="request-actions">
            <button class="request-secondary" type="button" data-request-back>Voltar</button>
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

    const selectedProduct = openButton.dataset.trackSelectedCategory;
    
    let pageCategory = null;
    if (document.body.classList.contains('jiu-jitsu-page')) pageCategory = 'jiu-jitsu';
    else if (document.body.classList.contains('muay-thai-page')) pageCategory = 'muay-thai';
    else {
      const path = window.location.pathname;
      if (path.includes('/jiu-jitsu')) pageCategory = 'jiu-jitsu';
      else if (path.includes('/muay-thai')) pageCategory = 'muay-thai';
    }

    openRequestModal(selectedProduct, pageCategory);
  });

  document.querySelectorAll('[data-request-close]').forEach(button => {
    button.addEventListener('click', closeRequestModal);
  });
  document.querySelector('[data-request-back]').addEventListener('click', goBack);
  document.querySelector('[data-request-next]').addEventListener('click', goNext);
  document.getElementById('customer-request-form').addEventListener('click', handleRequestFormClick);
  document.getElementById('customer-request-form').addEventListener('change', handleFormChange);
}

function openRequestModal(selectedProduct = null, pageCategory = null) {
  const modal = document.getElementById('customer-request-modal');
  modal.setAttribute('aria-hidden', 'false');
  document.body.classList.add('request-modal-open');
  state.step = 1;
  state.status = REQUEST_STATUS.idle;
  document.querySelector('[data-request-next]').disabled = false;
  document.querySelector('.request-actions').style.display = 'flex';

  if (selectedProduct) {
    let mappedModality = null;
    if (pageCategory === 'jiu-jitsu') mappedModality = MODALITY.jiuJitsu;
    else if (pageCategory === 'muay-thai') mappedModality = MODALITY.muayThaiBoxe;
    
    if (!mappedModality) {
      for (const [mod, list] of Object.entries(options.productTypes)) {
        if (list.includes(selectedProduct)) {
          mappedModality = mod;
          break;
        }
      }
    }

    if (mappedModality) {
      const itemKey = `${mappedModality}::${selectedProduct}`;
      state.data[FIELD.productTypes] = state.data[FIELD.productTypes] || [];
      if (!state.data[FIELD.productTypes].includes(itemKey)) {
        state.data[FIELD.productTypes].push(itemKey);
      }
    }
  } else if (state.data[FIELD.productTypes] === undefined) {
    state.data[FIELD.productTypes] = [];
  }

  renderStep();
}

function closeRequestModal() {
  document.getElementById('customer-request-modal').setAttribute('aria-hidden', 'true');
  document.body.classList.remove('request-modal-open');
}

function handleRequestFormClick(event) {
  if (!(event.target instanceof Element)) return;

  const addProductsButton = event.target.closest('[data-request-add-products]');
  if (!addProductsButton) return;

  saveCurrentStep();
  state.step = 1;
  renderStep();
}

function goBack() {
  if (state.step === 1) return;
  state.step -= 1;
  renderStep();
}

async function goNext() {
  saveCurrentStep();

  if (!isCurrentStepValid()) return;

  if (state.step < TOTAL_STEPS) {
    state.step += 1;
    renderStep();
    return;
  }

  await submitRequest();
}

function saveCurrentStep() {
  const form = document.getElementById('customer-request-form');
  const formData = new FormData(form);

  if (state.step === 1) {
    const selectedKeys = formData.getAll(FIELD.productTypes).map(value => value.toString());
    state.data[FIELD.productTypes] = selectedKeys;

    state.data[FIELD.productDetails] = Object.fromEntries(
      Object.entries(state.data[FIELD.productDetails] || {}).filter(([key]) => selectedKeys.includes(key))
    );
    return;
  }

  if (state.step === 2) {
    state.data[FIELD.productDetails] = {};
    formData.forEach((value, key) => {
      const parts = key.split(DETAIL_FIELD_SEPARATOR);
      if (parts.length < 3) return;
      const field = parts[0];
      const itemKey = parts.slice(1).join(DETAIL_FIELD_SEPARATOR);
      
      state.data[FIELD.productDetails][itemKey] = state.data[FIELD.productDetails][itemKey] || {};
      state.data[FIELD.productDetails][itemKey][field] = value.toString();
    });
    return;
  }

  formData.forEach((value, key) => {
    state.data[key] = value.toString();
  });

  state.data[FIELD.acceptedPrivacyPolicy] = formData.get(FIELD.acceptedPrivacyPolicy) === 'true';
}

function isCurrentStepValid() {
  const form = document.getElementById('customer-request-form');
  const formData = new FormData(form);

  if (state.step === 1) {
    const selected = formData.getAll(FIELD.productTypes);
    if (selected.length === 0) {
      document.querySelector('.request-error')?.remove();
      document.getElementById('request-step-content').insertAdjacentHTML('beforeend', renderError('Selecione pelo menos um produto para continuar.'));
      return false;
    }
    return true;
  }

  if (state.step === 2) {
    return true;
  }

  if (state.step === 3) {
    const name = formData.get(FIELD.customerName)?.toString().trim();
    const phone = formData.get(FIELD.customerPhone)?.toString().trim();

    let missing = false;
    if (!name || !phone) {
      missing = true;
    }

    if (missing) {
      document.querySelector('.request-error')?.remove();
      document.getElementById('request-step-content').insertAdjacentHTML('beforeend', renderError(ERROR_MESSAGES.required));
      return false;
    }

    return true;
  }

  return true;
}

function renderStep() {
  document.getElementById('request-step').textContent = state.step;
  document.querySelector('[data-request-back]').style.visibility = state.step === 1 ? 'hidden' : 'visible';
  document.querySelector('[data-request-next]').textContent = state.step === TOTAL_STEPS ? BUTTON_LABELS.submit : BUTTON_LABELS.continue;

  const content = document.getElementById('request-step-content');
  content.innerHTML = getStepHtml();
  updateSubmitButtonState();
}

function handleFormChange(event) {
  if (!(event.target instanceof HTMLInputElement)) return;
  if (event.target.name !== FIELD.acceptedPrivacyPolicy) return;

  state.data[FIELD.acceptedPrivacyPolicy] = event.target.checked;
  updateSubmitButtonState();
  document.querySelector('.request-error')?.remove();
}

function updateSubmitButtonState() {
  const button = document.querySelector('[data-request-next]');

  if (state.step !== TOTAL_STEPS || state.status === REQUEST_STATUS.success) {
    button.disabled = false;
    return;
  }

  button.disabled = state.data[FIELD.acceptedPrivacyPolicy] !== true;
}

function getStepHtml() {
  if (state.status === REQUEST_STATUS.success) {
    return '<div class="request-success"><strong>Pedido recebido.</strong><span>Vamos confirmar disponibilidade pelo WhatsApp.</span></div>';
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
  const selectedKeys = getSelectedProductTypes();
  
  return `
    <fieldset class="request-choice-group">
      <legend>O que você quer encontrar?</legend>
      <p class="request-help">Selecione um ou mais itens abaixo. Você pode escolher produtos de diferentes modalidades.</p>
      
      ${Object.entries(options.productTypes).map(([modality, types]) => `
        <div class="request-modality-group">
          <h4 class="request-group-title">${modality}</h4>
          <div class="request-choice-grid">
            ${types.map(type => {
              const itemKey = `${modality}::${type}`;
              const isChecked = selectedKeys.includes(itemKey);
              return `
                <label class="request-choice request-choice-multiple ${isChecked ? 'selected' : ''}">
                  <input type="checkbox" name="${FIELD.productTypes}" value="${itemKey}" ${isChecked ? 'checked' : ''}>
                  <span>${type}</span>
                </label>
              `;
            }).join('')}
          </div>
        </div>
      `).join('')}
    </fieldset>
  `;
}

function renderProductQuestions() {
  const selectedKeys = getSelectedProductTypes();

  if (selectedKeys.length === 0) {
    return '<p class="request-help">Selecione pelo menos um produto no passo anterior.</p>';
  }

  return selectedKeys.map(key => {
    const [modality, productType] = key.split(DETAIL_FIELD_SEPARATOR);
    return renderProductQuestionGroup(key, modality, productType);
  }).join('');
}

function renderProductQuestionGroup(key, modality, productType) {
  const sizeOptions = options.sizes[productType];
  const beltColorOptions = getBeltColorOptions(productType, modality);
  const needsBodyInfo = productType === PRODUCT_TYPE.kimonoAdulto || productType === PRODUCT_TYPE.kimonoInfantilJudo;
  const hasStructuredOptions = Boolean(sizeOptions || beltColorOptions || needsBodyInfo || productType === PRODUCT_TYPE.kimonoInfantilJudo);
  const details = state.data[FIELD.productDetails]?.[key] || {};

  return `
    <section class="request-product-section">
      <h3>${productType} <span class="request-badge">${modality}</span></h3>
      ${sizeOptions ? renderDetailChoiceGroup(FIELD.size, key, 'Tamanho', sizeOptions, details[FIELD.size]) : ''}
      ${beltColorOptions ? renderDetailChoiceGroup(FIELD.color, key, 'Cor da faixa', beltColorOptions, details[FIELD.color], getBeltColorIconSrc) : ''}
      ${needsBodyInfo ? '<p class="request-help">Para kimono, altura e peso ajudam a orientar o tamanho com mais segurança.</p>' : ''}
      ${needsBodyInfo ? '<label class="request-label">Altura em cm<input name="' + getDetailFieldName(FIELD.heightCm, key) + '" type="number" min="50" max="230" value="' + (details[FIELD.heightCm] || '') + '"></label>' : ''}
      ${needsBodyInfo ? '<label class="request-label">Peso em kg<input name="' + getDetailFieldName(FIELD.weightKg, key) + '" type="number" min="10" max="250" step="0.1" value="' + (details[FIELD.weightKg] || '') + '"></label>' : ''}
      ${productType === PRODUCT_TYPE.kimonoInfantilJudo ? '<label class="request-label">Idade<input name="' + getDetailFieldName(FIELD.age, key) + '" type="number" min="1" max="120" value="' + (details[FIELD.age] || '') + '"></label>' : ''}
      ${productType === PRODUCT_TYPE.kimonoInfantilJudo ? '<p class="request-help">Também temos opções infantis para Judô.</p>' : ''}
      ${!hasStructuredOptions ? renderProductDetailsField(key, details[FIELD.productDetails]) : ''}
    </section>
  `;
}

function getBeltColorOptions(productType, modality) {
  if (productType === PRODUCT_TYPE.faixaAdulto) return ADULT_BELT_COLORS;

  if (productType === PRODUCT_TYPE.faixaInfantil && modality === MODALITY.jiuJitsu) {
    return JIU_JITSU_KIDS_BELT_COLORS;
  }

  if (productType === PRODUCT_TYPE.faixaInfantil && modality === MODALITY.judo) {
    return JUDO_KIDS_BELT_COLORS;
  }

  return null;
}

function getDetailFieldName(field, key) {
  return `${field}${DETAIL_FIELD_SEPARATOR}${key}`;
}

function getBeltColorIconSrc(value) {
  const filename = value.toLowerCase().replace(/\s+e\s+/g, '-e-').replace(/\s+/g, '-');
  return `${BELT_ICON_BASE_PATH}${filename}.png`;
}

function renderDetailChoiceGroup(field, key, label, values, selectedValue, getIconSrc = null) {
  return renderChoiceGroup(getDetailFieldName(field, key), label, values, selectedValue, getIconSrc);
}

function renderChoiceGroup(name, label, values, selectedValue = null, getIconSrc = null) {
  return `
    <fieldset class="request-choice-group">
      <legend>${label}</legend>
      <div class="request-choice-grid">
        ${values.map(value => {
          const isSelected = selectedValue === value;
          return `
            <label class="request-choice ${isSelected ? 'selected' : ''}">
              <input type="radio" name="${name}" value="${value}" ${isSelected ? 'checked' : ''}>
              ${getIconSrc ? `<img src="${getIconSrc(value)}" alt="" aria-hidden="true">` : ''}
              <span>${value}</span>
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
      <textarea name="${name}" rows="3" maxlength="${PRODUCT_DETAILS_MAX_LENGTH}">${value || ''}</textarea>
    </label>
  `;
}

function renderReviewStep() {
  return `
    ${renderSelectedProductsSummary()}
    <label class="request-label">Seu nome<input name="${FIELD.customerName}" value="${state.data[FIELD.customerName] || ''}" required></label>
    <label class="request-label">WhatsApp<input name="${FIELD.customerPhone}" value="${state.data[FIELD.customerPhone] || ''}" inputmode="tel" required></label>
    <label class="request-label">Observações<textarea name="${FIELD.notes}" rows="3">${state.data[FIELD.notes] || ''}</textarea></label>
    ${renderPrivacyConsent()}
    <input class="request-honeypot" name="${FIELD.website}" tabindex="-1" autocomplete="off">
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
  const [modality, productType] = key.split(DETAIL_FIELD_SEPARATOR);
  const details = state.data[FIELD.productDetails]?.[key] || {};
  const summary = [
    ['Tamanho', details[FIELD.size]],
    ['Cor', details[FIELD.color]],
    ['Altura', details[FIELD.heightCm] ? `${details[FIELD.heightCm]} cm` : ''],
    ['Peso', details[FIELD.weightKg] ? `${details[FIELD.weightKg]} kg` : ''],
    ['Idade', details[FIELD.age]],
    ['Detalhes', details[FIELD.productDetails]],
  ].filter(([, value]) => value);

  return `
    <article class="request-selection-item">
      <strong>${productType} <span class="request-badge">${modality}</span></strong>
      ${summary.length > 0 ? `<dl>${summary.map(([label, value]) => `<div><dt>${label}</dt><dd>${value}</dd></div>`).join('')}</dl>` : '<span>Sem detalhes adicionais</span>'}
    </article>
  `;
}

function renderPrivacyConsent() {
  return `
    <section class="request-privacy" aria-label="Política de Privacidade">
      <label class="request-checkbox">
        <input type="checkbox" name="${FIELD.acceptedPrivacyPolicy}" value="true" ${state.data[FIELD.acceptedPrivacyPolicy] === true ? 'checked' : ''}>
        <span>Estou de acordo em compartilhar esses dados para que a Morita Fitness entre em contato comigo para prosseguir com o atendimento.</span>
      </label>
    </section>
  `;
}

async function submitRequest() {
  const button = document.querySelector('[data-request-next]');
  let whatsappWindow = null;

  if (state.data[FIELD.acceptedPrivacyPolicy] !== true) {
    document.querySelector('.request-error')?.remove();
    document.getElementById('request-step-content').insertAdjacentHTML('beforeend', renderError(ERROR_MESSAGES.privacy));
    updateSubmitButtonState();
    return;
  }

  button.disabled = true;
  button.textContent = BUTTON_LABELS.submitting;

  try {
    const payload = buildPayload();
    whatsappWindow = window.open('', '_blank');
    const response = await fetch(`${API_BASE_URL}/v1/CustomerProductRequest`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });

    if (!response.ok) throw new Error('Request failed');

    openWhatsAppRequestMessage(payload, whatsappWindow);
    state.status = REQUEST_STATUS.success;
    state.data = {};
    document.querySelector('.request-actions').style.display = 'none';
    document.getElementById('request-step-content').innerHTML = getStepHtml();
  } catch {
    whatsappWindow?.close();
    document.querySelector('.request-error')?.remove();
    document.getElementById('request-step-content').insertAdjacentHTML('beforeend', renderError(ERROR_MESSAGES.submit));
    button.disabled = false;
    button.textContent = BUTTON_LABELS.submit;
  }
}

function renderError(message) {
  return `<p class="request-error">${message}</p>`;
}

function buildPayload() {
  const selectedKeys = getSelectedProductTypes();
  return {
    customerName: state.data[FIELD.customerName],
    customerPhone: state.data[FIELD.customerPhone],
    modality: getSelectedModalities().join(', ') || 'Outro',
    notes: state.data[FIELD.notes] || null,
    website: state.data[FIELD.website] || null,
    acceptedPrivacyPolicy: true,
    source: 'landing-page',
    landingPage: getLandingPagePath(),
    campaign: getCampaign(),
    items: selectedKeys.map(key => {
      const [modality, productType] = key.split(DETAIL_FIELD_SEPARATOR);
      const details = state.data[FIELD.productDetails]?.[key] || {};

      return {
        productType,
        size: details[FIELD.size] || null,
        color: details[FIELD.color] || null,
        heightCm: details[FIELD.heightCm] ? Number(details[FIELD.heightCm]) : null,
        weightKg: details[FIELD.weightKg] ? Number(details[FIELD.weightKg]) : null,
        age: details[FIELD.age] ? Number(details[FIELD.age]) : null,
        notes: buildItemNotes(details),
      };
    }),
  };
}

function openWhatsAppRequestMessage(payload, whatsappWindow) {
  const url = "https://wa.me/" + WHATSAPP_NUMBER + "?text=" + encodeURIComponent(buildWhatsAppMessage(payload));

  if (whatsappWindow && !whatsappWindow.closed) {
    whatsappWindow.location.href = url;
    return;
  }

  window.open(url, "_blank", "noopener,noreferrer");
}

function buildWhatsAppMessage(request) {
  const lines = [
    "Nova consulta no site Morita",
    "",
    "Nome: " + valueOrDash(request.customerName),
    "Telefone: " + valueOrDash(request.customerPhone),
    "Modalidade: " + valueOrDash(request.modality),
    "Página: " + valueOrDash(request.landingPage),
    "Campanha: " + valueOrDash(request.campaign),
  ];

  if (request.notes) {
    lines.push("Observações: " + request.notes);
  }

  lines.push("", "Itens solicitados:");

  request.items.forEach((item, index) => {
    lines.push((index + 1) + ". " + valueOrDash(item.productType));
    lines.push("   Tamanho: " + valueOrDash(item.size));
    lines.push("   Cor: " + valueOrDash(item.color));

    if (item.heightCm) {
      lines.push("   Altura: " + item.heightCm + " cm");
    }

    if (item.weightKg) {
      lines.push("   Peso: " + item.weightKg + " kg");
    }

    if (item.age) {
      lines.push("   Idade: " + item.age);
    }

    if (item.notes) {
      lines.push("   Observações do item: " + item.notes);
    }
  });

  return lines.join(String.fromCharCode(10));
}

function valueOrDash(value) {
  return value || "-";
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
  const productDetails = details[FIELD.productDetails]?.trim();
  return productDetails ? `Detalhes do produto: ${productDetails}` : null;
}

function getSelectedProductTypes() {
  return Array.isArray(state.data[FIELD.productTypes]) ? state.data[FIELD.productTypes] : [];
}

function getSelectedModalities() {
  const items = getSelectedProductTypes();
  const modalities = items.map(item => item.split(DETAIL_FIELD_SEPARATOR)[0]);
  return [...new Set(modalities)];
}
