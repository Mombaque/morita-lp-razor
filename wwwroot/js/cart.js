const MIN_QUANTITY = 1;
const MAX_QUANTITY = 10;

function readQuantity(input) {
  const value = Number.parseInt(input.value, 10);
  return Number.isFinite(value) ? value : MIN_QUANTITY;
}

function normalizeQuantity(input) {
  input.value = String(Math.min(MAX_QUANTITY, Math.max(MIN_QUANTITY, readQuantity(input))));
}

function syncStepper(input) {
  const stepper = input.closest('[data-cart="quantity-stepper"]');
  if (!stepper) return;

  const value = Number.parseInt(input.value, 10);
  const decrement = stepper.querySelector('[data-cart="quantity-decrement"]');
  const increment = stepper.querySelector('[data-cart="quantity-increment"]');
  if (decrement) decrement.disabled = Number.isFinite(value) && value <= MIN_QUANTITY;
  if (increment) increment.disabled = Number.isFinite(value) && value >= MAX_QUANTITY;
}

function selectQuantity(input) {
  input.select();
}

document.querySelectorAll('[data-cart="quantity-input"]').forEach((input) => {
  input.addEventListener('focus', () => window.setTimeout(() => selectQuantity(input), 0));
  input.addEventListener('click', () => selectQuantity(input));
  input.addEventListener('input', () => syncStepper(input));
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
    const next = Math.min(MAX_QUANTITY, Math.max(MIN_QUANTITY, readQuantity(input) + delta));
    input.value = String(next);
    syncStepper(input);
    input.focus({ preventScroll: true });
  });
});
