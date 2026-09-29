import { ADULT_KIMONO_SIZE_CHART, KIDS_KIMONO_SIZE_CHART, suggestKimonoSize } from './kimono-size.js';

const AUDIENCES = {
  adulto: { chart: ADULT_KIMONO_SIZE_CHART, productType: 'Kimono Adulto' },
  infantil: { chart: KIDS_KIMONO_SIZE_CHART, productType: 'Kimono Infantil / Judô' },
};

const PLACEHOLDER_TEXT = 'Preencha altura e peso para ver a sugestão.';

function initCalculator(root) {
  const form = root.querySelector('form');
  const result = root.querySelector('[data-size-result]');
  const cta = root.querySelector('[data-size-cta]');
  if (!form || !result || !cta) return;

  const update = () => {
    const formData = new FormData(form);
    const audience = AUDIENCES[formData.get('audience')] || AUDIENCES.adulto;
    const heightCm = formData.get('heightCm')?.toString() || '';
    const weightKg = formData.get('weightKg')?.toString() || '';
    const size = suggestKimonoSize(audience.chart, heightCm, weightKg);

    cta.dataset.requestProduct = audience.productType;

    if (!size) {
      result.innerHTML = `<span class="size-calculator-placeholder">${PLACEHOLDER_TEXT}</span>`;
      cta.disabled = true;
      cta.textContent = 'Consultar disponibilidade';
      delete cta.dataset.requestPrefill;
      delete cta.dataset.trackSelectedCategory;
      return;
    }

    result.innerHTML = `<span>Tamanho sugerido</span><strong>${size}</strong>`;
    cta.disabled = false;
    cta.textContent = `Consultar disponibilidade no ${size}`;
    cta.dataset.trackSelectedCategory = `${audience.productType} ${size}`;
    cta.dataset.requestPrefill = JSON.stringify({ size, heightCm, weightKg });
  };

  form.addEventListener('input', update);
  form.addEventListener('change', update);
  form.addEventListener('submit', event => event.preventDefault());
  update();
}

function init() {
  document.querySelectorAll('[data-size-calculator]').forEach(initCalculator);
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
else init();
