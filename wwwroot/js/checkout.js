const form = document.querySelector('[data-fulfillment-form]');

if (form) {
  const methodInputs = [...form.querySelectorAll('input[name="FulfillmentMethod"]')];
  const panels = [...form.querySelectorAll('[data-fulfillment-panel]')];
  const shippingFields = [...form.querySelectorAll('[data-fulfillment-panel="shipping"] input[name^="ShippingAddress."]:not([name="ShippingAddress.Complement"])')];
  const addressChoices = [...form.querySelectorAll('input[name="SelectedAddressId"]')];
  const addressControls = form.querySelector('[data-new-address-controls]');
  const quoteShipping = form.querySelector('[data-quote-shipping]');
  let shippingQuoteContent = form.querySelector('[data-shipping-quote]');
  const paymentMethods = { pix: 'pix', card: 'card' };
  const paymentInputs = [...form.querySelectorAll('input[name="PaymentMethod"]')];
  const submit = form.querySelector('[data-checkout-submit]');
  const submitLabel = submit?.querySelector('[data-checkout-submit-label]');
  const feedback = form.querySelector('[data-checkout-feedback]');
  const summaryShippingValue = form.querySelector('[data-summary-shipping-value]');
  const summaryShippingDetail = form.querySelector('[data-summary-shipping-detail]');
  const checkoutReady = submit?.dataset.checkoutReady === 'true';
  const initialFeedback = feedback?.textContent?.trim() ?? '';
  const addressFieldNames = {
    recipient: 'ShippingAddress.Recipient',
    street: 'ShippingAddress.Street',
    number: 'ShippingAddress.Number',
    complement: 'ShippingAddress.Complement',
    neighborhood: 'ShippingAddress.Neighborhood',
    city: 'ShippingAddress.City',
    state: 'ShippingAddress.State',
    postalCode: 'ShippingAddress.PostalCode'
  };

  const setAddressFields = (choice) => {
    const values = choice?.dataset.newAddress === 'true'
      ? {}
      : choice?.dataset ?? {};
    Object.entries(addressFieldNames).forEach(([key, name]) => {
      const field = form.querySelector(`[name="${name}"]`);
      if (field) field.value = values[key] ?? '';
    });
    if (addressControls) addressControls.hidden = choice?.dataset.newAddress !== 'true';
  };

  const setQuoteStatus = (message, role = 'status') => {
    if (!shippingQuoteContent) return;
    shippingQuoteContent.replaceChildren();
    const status = document.createElement('p');
    status.className = role === 'alert' ? 'validation-error' : 'cart-estimate-note';
    status.dataset.shippingQuoteStatus = '';
    status.setAttribute('role', role);
    status.setAttribute('aria-live', 'polite');
    status.textContent = message;
    shippingQuoteContent.append(status);
  };

  const update = () => {
    const method = methodInputs.find((input) => input.checked)?.value;
    const hasShippingQuote = Boolean(form.querySelector('input[name="PublicShippingQuoteId"]:checked'));
    const selectedShippingOption = form.querySelector('input[name="PublicShippingQuoteId"]:checked');
    panels.forEach((panel) => {
      panel.hidden = panel.dataset.fulfillmentPanel !== method;
    });
    shippingFields.forEach((field) => {
      field.required = method === 'shipping' && field.name !== 'PublicShippingQuoteId';
    });
    const payment = paymentInputs.find((input) => input.checked)?.value;
    if (submitLabel) {
      submitLabel.textContent = payment === paymentMethods.card ? 'Continuar para o cartão' : 'Continuar para o PIX';
    }
    const disabled = !checkoutReady || !method || (method === 'shipping' && !hasShippingQuote);
    if (submit) {
      submit.disabled = disabled;
    }
    if (feedback) {
      const message = !checkoutReady
        ? initialFeedback || 'Não foi possível preparar o checkout. Atualize a página e tente novamente.'
        : !method
          ? 'Escolha uma forma de entrega para continuar.'
          : method === 'shipping' && !hasShippingQuote
            ? 'Calcule o frete e escolha uma opção de entrega.'
            : '';
      feedback.textContent = disabled ? message : '';
      feedback.hidden = !disabled;
    }
    if (summaryShippingValue && summaryShippingDetail) {
      summaryShippingValue.textContent = selectedShippingOption?.dataset.shippingPrice ?? 'Calcule pelo CEP';
      summaryShippingDetail.textContent = selectedShippingOption
        ? `${selectedShippingOption.dataset.shippingService} · ${selectedShippingOption.dataset.shippingDetail}`
        : '';
      summaryShippingDetail.hidden = !selectedShippingOption;
    }
  };

  const bindShippingQuoteChoices = () => {
    form.querySelectorAll('input[name="PublicShippingQuoteId"]').forEach((input) => input.addEventListener('change', update));
  };

  const requestShippingQuote = async () => {
    if (!quoteShipping || !shippingQuoteContent) return;
    const action = quoteShipping.getAttribute('formaction') || form.getAttribute('action') || window.location.href;
    const originalLabel = quoteShipping.textContent;
    quoteShipping.disabled = true;
    quoteShipping.textContent = 'Calculando frete...';
    quoteShipping.setAttribute('aria-busy', 'true');
    setQuoteStatus('Calculando frete...');
    update();

    try {
      const response = await fetch(action, {
        method: 'POST',
        body: new FormData(form),
        headers: {
          Accept: 'text/html',
          'X-Requested-With': 'XMLHttpRequest'
        }
      });
      const html = await response.text();
      if (!response.ok) throw new Error(`Shipping quote failed with status ${response.status}`);
      const template = document.createElement('template');
      template.innerHTML = html.trim();
      const updatedQuoteContent = template.content.querySelector('[data-shipping-quote]');
      if (!updatedQuoteContent) throw new Error('Shipping quote fragment was not returned');
      shippingQuoteContent.replaceWith(updatedQuoteContent);
      shippingQuoteContent = updatedQuoteContent;
      bindShippingQuoteChoices();
      update();
    } catch {
      setQuoteStatus('Não foi possível calcular o frete. Tente novamente.', 'alert');
      update();
    } finally {
      quoteShipping.disabled = false;
      quoteShipping.textContent = originalLabel;
      quoteShipping.removeAttribute('aria-busy');
    }
  };

  methodInputs.forEach((input) => input.addEventListener('change', update));
  paymentInputs.forEach((input) => input.addEventListener('change', update));
  bindShippingQuoteChoices();
  form.addEventListener('submit', (event) => {
    if (event.submitter !== quoteShipping) return;
    event.preventDefault();
    void requestShippingQuote();
  });
  addressChoices.forEach((choice) => choice.addEventListener('change', () => setAddressFields(choice)));
  const selectedAddress = addressChoices.find((choice) => choice.checked);
  if (addressChoices.length === 0) {
    if (addressControls) addressControls.hidden = false;
  } else {
    setAddressFields(selectedAddress);
  }
  update();
}
