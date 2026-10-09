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
  const postalStatus = form.querySelector('[data-cep-status]');
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

  const setField = (name, value) => {
    const field = form.querySelector(`[name="${name}"]`);
    if (field) field.value = value ?? '';
  };

  let lastLookup = '';
  const lookupPostalCode = async (digits) => {
    if (digits === lastLookup) return;
    lastLookup = digits;
    if (postalStatus) postalStatus.textContent = 'Buscando endereço...';
    try {
      const response = await fetch(`https://viacep.com.br/ws/${digits}/json/`, { headers: { Accept: 'application/json' } });
      const address = response.ok ? await response.json() : null;
      // Ignore a lookup that finished after the shopper moved on to another CEP.
      if (postalDigits() !== digits) return;
      if (!address || address.erro) {
        if (postalStatus) postalStatus.textContent = 'CEP não encontrado. Confira o número ou preencha o endereço.';
        return;
      }
      setField(addressFieldNames.street, address.logradouro);
      setField(addressFieldNames.neighborhood, address.bairro);
      setField(addressFieldNames.city, address.localidade);
      setField(addressFieldNames.state, address.uf);
      if (postalStatus) postalStatus.textContent = `${address.localidade}/${address.uf}`;
      const number = form.querySelector(`[name="${addressFieldNames.number}"]`);
      if (address.logradouro && number && !number.value) number.focus();
    } catch {
      // Address lookup is a convenience; the shopper can still type every field.
      if (postalStatus) postalStatus.textContent = '';
    }
  };

  let quotedPostal = '';
  let quoteTimer = 0;
  const scheduleQuote = () => {
    window.clearTimeout(quoteTimer);
    const digits = postalDigits();
    if (quotePostal) quotePostal.textContent = digits.length === 8 ? `CEP ${formatPostal(digits)}` : 'Informe o CEP no endereço de entrega.';
    if (digits.length !== 8 || digits === quotedPostal) return;
    quoteTimer = window.setTimeout(() => {
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
    if (digits.length === 8) void lookupPostalCode(digits);
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
  // Pickup orders ask for a billing address for the nota fiscal; the CEP fills it like the shipping address.
  const billingPostal = form.querySelector('[data-billing-postal-code]');
  const billingStatus = form.querySelector('[data-billing-cep-status]');
  let lastBillingLookup = '';
  const lookupBillingPostalCode = async () => {
    const digits = (billingPostal?.value ?? '').replace(/\D/g, '').slice(0, 8);
    if (digits.length !== 8 || digits === lastBillingLookup) return;
    lastBillingLookup = digits;
    if (billingStatus) billingStatus.textContent = 'Buscando endereço...';
    try {
      const response = await fetch(`https://viacep.com.br/ws/${digits}/json/`, { headers: { Accept: 'application/json' } });
      const address = response.ok ? await response.json() : null;
      if (!address || address.erro) {
        if (billingStatus) billingStatus.textContent = 'CEP não encontrado. Confira o número ou preencha o endereço.';
        return;
      }
      setField('BillingAddress.Street', address.logradouro);
      setField('BillingAddress.Neighborhood', address.bairro);
      setField('BillingAddress.City', address.localidade);
      setField('BillingAddress.State', address.uf);
      if (billingStatus) billingStatus.textContent = `${address.localidade}/${address.uf}`;
    } catch {
      if (billingStatus) billingStatus.textContent = '';
    }
  };
  if (billingPostal) {
    billingPostal.addEventListener('input', () => {
      const digits = billingPostal.value.replace(/\D/g, '').slice(0, 8);
      billingPostal.value = formatPostal(digits);
      lookupBillingPostalCode();
    });
  }
  form.addEventListener('input', (event) => {
    const name = event.target?.name;
    if (!name) return;
    form.querySelectorAll(`[data-valmsg-for="${CSS.escape(name)}"]`).forEach((message) => { message.textContent = ''; });
  });
  update();
  if (methodInputs.find((input) => input.checked)?.value === 'shipping') scheduleQuote();
}
