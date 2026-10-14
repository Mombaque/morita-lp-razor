const NOT_FOUND_MESSAGE = 'CEP não encontrado. Confira o número.';

const postalDigits = (value) => (value ?? '').replace(/\D/g, '').slice(0, 8);

const formatPostal = (digits) => (digits.length > 5 ? `${digits.slice(0, 5)}-${digits.slice(5)}` : digits);

const fetchAddress = async (digits) => {
  const response = await fetch(`/address/postal-code/${digits}`, { headers: { Accept: 'application/json' } });
  if (response.status === 404) return { found: false };
  if (!response.ok) throw new Error(`Postal code lookup failed with status ${response.status}`);
  return { found: true, address: await response.json() };
};

export function initAddressCep(group) {
  const cep = group.querySelector('[data-cep-input]');
  const message = group.querySelector('[data-cep-message]');
  const details = group.querySelector('[data-address-details]');
  if (!cep || !details) return { sync: () => {}, isBlocked: () => false };
  const detailFields = [...details.querySelectorAll('input, select')];
  const fieldByName = (name) => group.querySelector(`[data-address-field="${name}"]`);
  const street = fieldByName('street');
  const number = fieldByName('number');
  const city = fieldByName('city');
  const state = fieldByName('state');

  let lastLookup = '';
  let resolvedDigits = '';
  let notFoundDigits = '';

  const isBlocked = () => notFoundDigits !== '' && postalDigits(cep.value) === notFoundDigits;

  const setMessage = (text) => {
    if (message) message.textContent = text;
  };

  const sync = () => {
    const digits = postalDigits(cep.value);
    const locked = resolvedDigits !== '' && digits === resolvedDigits;
    const blocked = isBlocked();
    const filled = detailFields.some((field) => field.value.trim() !== '');
    details.hidden = blocked || (digits.length !== 8 && !filled);
    cep.classList.toggle('is-invalid', blocked);
    cep.setAttribute('aria-invalid', String(blocked));
    message?.classList.toggle('is-error', blocked);
    if (city) city.readOnly = locked;
    if (state) {
      state.classList.toggle('is-locked', locked);
      state.setAttribute('aria-disabled', String(locked));
      state.tabIndex = locked ? -1 : 0;
    }
    group.dispatchEvent(new CustomEvent('address-cep:change', { bubbles: true }));
  };

  const fill = (name, value) => {
    const field = fieldByName(name);
    if (!field || !value) return;
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true }));
  };

  const fallback = (text) => {
    resolvedDigits = '';
    setMessage(text);
    sync();
  };

  const lookup = async (digits) => {
    if (digits === lastLookup) return;
    lastLookup = digits;
    setMessage('Buscando endereço...');
    try {
      const result = await fetchAddress(digits);
      if (postalDigits(cep.value) !== digits) return;
      if (!result.found) {
        notFoundDigits = digits;
        fallback(NOT_FOUND_MESSAGE);
        return;
      }
      const { address } = result;
      fill('street', address.street);
      fill('neighborhood', address.neighborhood);
      fill('city', address.city);
      fill('state', address.state);
      resolvedDigits = digits;
      setMessage(`${address.city}/${address.state}`);
      sync();
      const next = street && !street.value ? street : number && !number.value ? number : null;
      next?.focus();
    } catch {
      if (postalDigits(cep.value) !== digits) return;
      lastLookup = '';
      fallback('Não foi possível buscar o CEP agora. Preencha o endereço.');
    }
  };

  cep.addEventListener('input', () => {
    const digits = postalDigits(cep.value);
    cep.value = formatPostal(digits);
    if (digits.length === 8) {
      void lookup(digits);
    } else {
      lastLookup = '';
      resolvedDigits = '';
      setMessage('');
    }
    sync();
  });

  group.closest('form')?.addEventListener('submit', (event) => {
    if (!isBlocked() || group.closest('[hidden]') || event.submitter?.hasAttribute('formnovalidate')) return;
    event.preventDefault();
    setMessage(NOT_FOUND_MESSAGE);
    cep.focus();
  });

  cep.value = formatPostal(postalDigits(cep.value));
  sync();
  return { sync, isBlocked };
}

document.querySelectorAll('[data-address-cep-auto]').forEach((group) => initAddressCep(group));
