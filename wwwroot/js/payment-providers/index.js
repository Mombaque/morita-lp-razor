const adapters = {
  mercadopago: () => import('./mercadopago.js'),
  fake: () => import('./fake.js')
};

export const mountCardForm = async (panel, onSubmit) => {
  const load = adapters[panel.dataset.provider];
  if (!load) throw new Error('Unsupported payment provider');
  const mount = panel.querySelector('[data-card-mount]');
  if (!mount) throw new Error('Card form mount point is missing');
  const adapter = await load();
  return adapter.mount(mount, {
    publicKey: panel.dataset.publicKey || '',
    amount: Number(panel.dataset.amount),
    maxInstallments: Number(panel.dataset.maxInstallments) || 1,
    payerEmail: panel.dataset.payerEmail || '',
    onSubmit
  });
};
