const form = document.querySelector('[data-fulfillment-form]');

if (form) {
  const methodInputs = [...form.querySelectorAll('input[name="FulfillmentMethod"]')];
  const panels = [...form.querySelectorAll('[data-fulfillment-panel]')];
  const shippingFields = [...form.querySelectorAll('[data-fulfillment-panel="shipping"] input:not([name="ShippingAddress.Complement"])')];
  const submit = form.querySelector('[data-checkout-submit]');
  const checkoutReady = submit?.dataset.checkoutReady === 'true';

  const update = () => {
    const method = methodInputs.find((input) => input.checked)?.value;
    panels.forEach((panel) => {
      panel.hidden = panel.dataset.fulfillmentPanel !== method;
    });
    shippingFields.forEach((field) => {
      field.required = method === 'shipping' && field.name !== 'PublicShippingQuoteId';
    });
    if (submit) {
      const hasShippingQuote = Boolean(form.querySelector('input[name="PublicShippingQuoteId"]:checked'));
      submit.disabled = !checkoutReady || !method || (method === 'shipping' && !hasShippingQuote);
    }
  };

  methodInputs.forEach((input) => input.addEventListener('change', update));
  form.querySelectorAll('input[name="PublicShippingQuoteId"]').forEach((input) => input.addEventListener('change', update));
  update();
}
