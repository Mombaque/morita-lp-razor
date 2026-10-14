import { initAddressCep } from './address-cep.js';

const form = document.querySelector('[data-fulfillment-form]');

if (form) {
  const methodInputs = [...form.querySelectorAll('input[name="FulfillmentMethod"]')];
  const panels = [...form.querySelectorAll('[data-fulfillment-panel]')];
  const shippingFields = [...form.querySelectorAll('[data-fulfillment-panel="shipping"] :is(input, select)[name^="ShippingAddress."]:not([name="ShippingAddress.Complement"])')];
  const addressChoices = [...form.querySelectorAll('input[name="SelectedAddressId"]')];
  const addressControls = form.querySelector('[data-new-address-controls]');
  const quoteShipping = form.querySelector('[data-quote-shipping]');
  const quoteShippingLabel = quoteShipping?.textContent ?? 'Calcular frete';
  let shippingQuoteContent = form.querySelector('[data-shipping-quote]');
  const paymentMethods = { pix: 'pix', card: 'card' };
  const paymentInputs = [...form.querySelectorAll('input[name="PaymentMethod"]')];
  const submit = form.querySelector('[data-checkout-submit]');
  const submitLabel = submit?.querySelector('[data-checkout-submit-label]');
  const feedback = form.querySelector('[data-checkout-feedback]');
  const summaryShippingValue = form.querySelector('[data-summary-shipping-value]');
  const summaryShippingDetail = form.querySelector('[data-summary-shipping-detail]');
  const summaryTotal = form.querySelector('[data-summary-total]');
  const actionsTotalRow = form.querySelector('[data-actions-total-row]');
  const actionsTotal = form.querySelector('[data-actions-total]');
  const summaryAmounts = form.querySelector('[data-merchandise-amount]');
  const summaryDetails = form.querySelector('[data-summary-details]');
  const postalInput = form.querySelector('[data-cep-input]');
  const shippingAddressGroup = form.querySelector('[data-shipping-address-group]');
  const shippingAddress = shippingAddressGroup ? initAddressCep(shippingAddressGroup) : null;
  const billingAddressGroup = form.querySelector('[data-billing-address-group]');
  const billingAddress = billingAddressGroup ? initAddressCep(billingAddressGroup) : null;
  const quotePostal = form.querySelector('[data-quote-postal]');
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
    shippingAddress?.sync();
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

  const formatMoney = (amount) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: summaryAmounts?.dataset.currency || 'BRL' }).format(amount);

  // The total shown here is a preview; the server reconfirms price, stock and freight before reserving.
  const updateTotals = (method, selectedShippingOption) => {
    const merchandise = Number(summaryAmounts?.dataset.merchandiseAmount);
    if (!summaryAmounts || !Number.isFinite(merchandise)) return;
    const freight = method === 'pickup' ? 0 : Number(selectedShippingOption?.dataset.shippingAmount);
    const known = method === 'pickup' || (method === 'shipping' && Number.isFinite(freight));
    if (summaryTotal) summaryTotal.textContent = known ? formatMoney(merchandise + freight) : 'Calcule o frete';
    if (actionsTotal) actionsTotal.textContent = known ? formatMoney(merchandise + freight) : '';
    if (actionsTotalRow) actionsTotalRow.hidden = !known;
  };

  const postalDigits = () => (postalInput?.value ?? '').replace(/\D/g, '').slice(0, 8);

  const formatPostal = (digits) => (digits.length > 5 ? `${digits.slice(0, 5)}-${digits.slice(5)}` : digits);

  const validationSummary = form.querySelector('[data-valmsg-summary]');
  const clearFieldError = (name) => {
    form.querySelectorAll(`[data-valmsg-for="${CSS.escape(name)}"]`).forEach((message) => {
      const text = message.textContent.trim();
      message.textContent = '';
      if (!text || !validationSummary) return;
      validationSummary.querySelectorAll('li').forEach((item) => {
        if (item.textContent.trim() === text) item.remove();
      });
      if (validationSummary.querySelector('li')) return;
      validationSummary.replaceChildren();
      const banner = document.querySelector('[data-checkout="error"][data-field-errors]');
      if (!banner) return;
      const bannerText = banner.textContent.trim();
      banner.remove();
      form.querySelectorAll('[data-shipping-quote-error]').forEach((error) => {
        if (error.textContent.trim() === bannerText) error.remove();
      });
    });
  };

  let quotedPostal = '';
  let quoteTimer = 0;
  const scheduleQuote = () => {
    window.clearTimeout(quoteTimer);
    const digits = postalDigits();
    if (quotePostal) quotePostal.textContent = digits.length === 8 ? `CEP ${formatPostal(digits)}` : 'Informe o CEP no endereço de entrega.';
    if (digits.length !== 8 || digits === quotedPostal) return;
    quoteTimer = window.setTimeout(() => {
      if (shippingAddress?.isBlocked()) return;
      quotedPostal = digits;
      void requestShippingQuote();
    }, 300);
  };

  const onPostalInput = () => {
    const digits = postalDigits();
    postalInput.value = formatPostal(digits);
    if (digits !== quotedPostal && form.querySelector('input[name="PublicShippingQuoteId"]')) {
      // A quote belongs to one CEP; drop it as soon as the CEP changes so it cannot be submitted.
      quotedPostal = '';
      setQuoteStatus(digits.length === 8 ? 'Calculando frete...' : 'Informe o CEP completo para calcular o frete.');
      update();
    }
    scheduleQuote();
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
    const cepBlocked = (method === 'shipping' && shippingAddress?.isBlocked()) || (method === 'pickup' && billingAddress?.isBlocked());
    const disabled = !checkoutReady || !method || cepBlocked || (method === 'shipping' && !hasShippingQuote);
    if (submit) {
      submit.disabled = disabled;
    }
    if (feedback) {
      const message = !checkoutReady
        ? initialFeedback || 'Não foi possível preparar o checkout. Atualize a página e tente novamente.'
        : !method
          ? 'Escolha uma forma de entrega para continuar.'
          : cepBlocked
            ? 'Informe um CEP válido para continuar.'
            : method === 'shipping' && !hasShippingQuote
              ? 'Calcule o frete e escolha uma opção de entrega.'
              : '';
      feedback.textContent = disabled ? message : '';
      feedback.hidden = !disabled;
    }
    if (summaryShippingValue && summaryShippingDetail) {
      summaryShippingValue.textContent = method === 'pickup'
        ? 'Grátis (retirada)'
        : selectedShippingOption?.dataset.shippingPrice ?? 'Calcule pelo CEP';
      summaryShippingDetail.textContent = selectedShippingOption
        ? `${selectedShippingOption.dataset.shippingService} · ${selectedShippingOption.dataset.shippingDetail}`
        : '';
      summaryShippingDetail.hidden = !selectedShippingOption;
    }
    updateTotals(method, selectedShippingOption);
  };

  const bindShippingQuoteChoices = () => {
    form.querySelectorAll('input[name="PublicShippingQuoteId"]').forEach((input) => input.addEventListener('change', update));
  };

  const requestShippingQuote = async () => {
    if (!quoteShipping || !shippingQuoteContent) return;
    const action = quoteShipping.getAttribute('formaction') || form.getAttribute('action') || window.location.href;
    const requestedPostal = postalDigits();
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
      // A newer CEP has its own request in flight; this response would show stale options.
      if (postalInput && postalDigits() !== requestedPostal) return;
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
      quotedPostal = '';
      setQuoteStatus('Não foi possível calcular o frete. Tente novamente.', 'alert');
      update();
    } finally {
      quoteShipping.disabled = false;
      quoteShipping.textContent = quoteShippingLabel;
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
  addressChoices.forEach((choice) => choice.addEventListener('change', () => {
    setAddressFields(choice);
    if (postalInput) postalInput.value = formatPostal(postalDigits());
    scheduleQuote();
  }));
  postalInput?.addEventListener('input', onPostalInput);
  methodInputs.forEach((input) => input.addEventListener('change', () => {
    if (input.checked && input.value === 'shipping') scheduleQuote();
  }));
  if (summaryDetails && window.matchMedia('(max-width: 800px)').matches) summaryDetails.open = false;
  const selectedAddress = addressChoices.find((choice) => choice.checked);
  if (addressChoices.length === 0) {
    if (addressControls) addressControls.hidden = false;
  } else {
    setAddressFields(selectedAddress);
  }
  if (postalInput) {
    postalInput.value = formatPostal(postalDigits());
    // A quote rendered by the server already matches the current CEP.
    if (form.querySelector('input[name="PublicShippingQuoteId"]')) quotedPostal = postalDigits();
  }
  form.addEventListener('address-cep:change', (event) => {
    if (event.target === shippingAddressGroup && shippingAddress?.isBlocked()) {
      window.clearTimeout(quoteTimer);
      setQuoteStatus('Informe um CEP válido para calcular o frete.');
    }
    update();
  });
  form.addEventListener('input', (event) => {
    const name = event.target?.name;
    if (name) clearFieldError(name);
  });
  update();
  if (methodInputs.find((input) => input.checked)?.value === 'shipping') scheduleQuote();
}
