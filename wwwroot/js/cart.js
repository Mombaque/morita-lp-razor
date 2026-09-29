const MIN_QUANTITY = 1;
const AUTO_SUBMIT_DELAY_MS = 500;

function maxQuantity(input) {
  return Number.parseInt(input.max, 10) || 10;
}

function readQuantity(input) {
  const value = Number.parseInt(input.value, 10);
  return Number.isFinite(value) ? value : MIN_QUANTITY;
}

function normalizeQuantity(input) {
  input.value = String(Math.min(maxQuantity(input), Math.max(MIN_QUANTITY, readQuantity(input))));
}

function syncStepper(input) {
  const stepper = input.closest('[data-cart="quantity-stepper"]');
  if (!stepper) return;

  const value = Number.parseInt(input.value, 10);
  const decrement = stepper.querySelector('[data-cart="quantity-decrement"]');
  const increment = stepper.querySelector('[data-cart="quantity-increment"]');
  if (decrement) decrement.disabled = Number.isFinite(value) && value <= MIN_QUANTITY;
  if (increment) increment.disabled = Number.isFinite(value) && value >= maxQuantity(input);
}

const submitTimers = new WeakMap();

// Quantity changes are saved automatically; the "Atualizar" button stays only as the no-JS path.
function scheduleSubmit(input) {
  const form = input.closest('form');
  if (!form) return;
  window.clearTimeout(submitTimers.get(input));
  submitTimers.set(input, window.setTimeout(() => {
    normalizeQuantity(input);
    if (input.value === input.defaultValue) return;
    form.querySelectorAll('button').forEach((button) => { button.disabled = true; });
    form.setAttribute('aria-busy', 'true');
    form.submit();
  }, AUTO_SUBMIT_DELAY_MS));
}

function selectQuantity(input) {
  input.select();
}

document.querySelectorAll('[data-cart="quantity-input"]').forEach((input) => {
  input.addEventListener('focus', () => window.setTimeout(() => selectQuantity(input), 0));
  input.addEventListener('click', () => selectQuantity(input));
  input.addEventListener('input', () => {
    syncStepper(input);
    if (input.value !== '') scheduleSubmit(input);
  });
  input.addEventListener('blur', () => {
    normalizeQuantity(input);
    syncStepper(input);
  });

  const form = input.closest('form');
  form?.addEventListener('submit', () => normalizeQuantity(input));
  syncStepper(input);
});

document.querySelectorAll('[data-cart="quantity-stepper"]').forEach((stepper) => {
  const input = stepper.querySelector('[data-cart="quantity-input"]');
  if (!input) return;

  stepper.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-cart]');
    if (!button || button.disabled) return;

    const delta = button.dataset.cart === 'quantity-increment' ? 1 : -1;
    const next = Math.min(maxQuantity(input), Math.max(MIN_QUANTITY, readQuantity(input) + delta));
    input.value = String(next);
    syncStepper(input);
    input.focus({ preventScroll: true });
    scheduleSubmit(input);
  });
});

document.querySelectorAll('[data-cart="update"]').forEach((button) => { button.hidden = true; });

document.querySelectorAll('form[data-confirm]').forEach((form) => {
  form.addEventListener('submit', (event) => {
    if (!window.confirm(form.dataset.confirm)) event.preventDefault();
  });
});
