const SDK_URL = 'https://sdk.mercadopago.com/js/v2';
let sdkLoading = null;

const loadSdk = () => {
  if (window.MercadoPago) return Promise.resolve(window.MercadoPago);
  sdkLoading ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = SDK_URL;
    script.async = true;
    script.onload = () => (window.MercadoPago ? resolve(window.MercadoPago) : reject(new Error('Mercado Pago SDK unavailable')));
    script.onerror = () => {
      sdkLoading = null;
      reject(new Error('Mercado Pago SDK failed to load'));
    };
    document.head.append(script);
  });
  return sdkLoading;
};

const toPayload = (data) => ({
  token: data.token,
  paymentMethodId: data.payment_method_id,
  issuerId: data.issuer_id ? String(data.issuer_id) : null,
  installments: Number(data.installments),
  payerEmail: data.payer?.email,
  identificationType: data.payer?.identification?.type,
  identificationNumber: data.payer?.identification?.number
});

export const mount = async (container, options) => {
  const MercadoPago = await loadSdk();
  const mp = new MercadoPago(options.publicKey, { locale: 'pt-BR' });
  const controller = await mp.bricks().create('cardPayment', container.id, {
    initialization: {
      amount: options.amount,
      payer: { email: options.payerEmail }
    },
    customization: {
      paymentMethods: {
        maxInstallments: options.maxInstallments,
        types: { excluded: ['debit_card', 'prepaid_card'] }
      },
      visual: { hideFormTitle: true }
    },
    callbacks: {
      onReady: () => container.dispatchEvent(new CustomEvent('card-form-ready', { bubbles: true })),
      onSubmit: (data) => options.onSubmit(toPayload(data)),
      onError: () => container.dispatchEvent(new CustomEvent('card-form-error', { bubbles: true }))
    }
  });
  return { unmount: () => controller.unmount() };
};
