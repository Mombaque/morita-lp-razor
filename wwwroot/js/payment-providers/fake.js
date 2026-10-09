const outcomes = [
  ['approve', 'Aprovar'],
  ['reject', 'Recusar'],
  ['pending', 'Deixar pendente']
];

export const mount = async (container, options) => {
  const form = document.createElement('form');
  form.className = 'fake-card-form';
  form.dataset.fakeCardForm = '';
  form.innerHTML = `
    <p class="cart-estimate-note">Provedor de teste: nenhum cartão real é usado.</p>
    <label class="checkout-field">Resultado<select class="checkout-control" name="outcome"></select></label>
    <label class="checkout-field">Parcelas<select class="checkout-control" name="installments"></select></label>
    <label class="checkout-field">CPF do titular<input class="checkout-control" name="document" inputmode="numeric" value="52998224725"></label>
    <button class="commerce-button commerce-button-primary" type="submit">Pagar com cartão</button>`;
  const outcome = form.elements.namedItem('outcome');
  outcomes.forEach(([value, label]) => outcome.append(new Option(label, value)));
  const installments = form.elements.namedItem('installments');
  for (let count = 1; count <= options.maxInstallments; count += 1) installments.append(new Option(`${count}x`, String(count)));

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const button = form.querySelector('button');
    button.disabled = true;
    try {
      await options.onSubmit({
        token: `fake_tok_${outcome.value}_${crypto.randomUUID().replaceAll('-', '')}`,
        paymentMethodId: 'visa',
        issuerId: null,
        installments: Number(installments.value),
        payerEmail: options.payerEmail,
        identificationType: 'CPF',
        identificationNumber: form.elements.namedItem('document').value
      });
    } catch {
      button.disabled = false;
    }
  });

  container.replaceChildren(form);
  container.dispatchEvent(new CustomEvent('card-form-ready', { bubbles: true }));
  return { unmount: () => form.remove() };
};
