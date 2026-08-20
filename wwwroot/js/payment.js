const card = document.querySelector('[data-payment-status]');
const copy = document.querySelector('[data-copy-pix]');
if (copy) copy.addEventListener('click', async () => {
  const value = document.querySelector('#pix-copy')?.value;
  if (!value) return;
  try { await navigator.clipboard.writeText(value); copy.textContent = 'Código copiado'; }
  catch { copy.textContent = 'Selecione e copie o código'; }
});

if (card && ['pending', 'processing', 'approved', 'conversionpending'].includes(card.dataset.paymentStatus)) {
  let attempts = 0;
  let timer = null;
  const poll = async () => {
    timer = null;
    if (document.visibilityState === 'hidden' || attempts >= 20) return;
    attempts++;
    try {
      const response = await fetch(`${location.pathname}?handler=Payment`, { headers: { Accept: 'application/json' }, credentials: 'same-origin' });
      if (!response.ok) return schedule();
      const result = await response.json();
      if (result.state === 'converted' && result.url) { location.assign(result.url); return; }
      if (['failed', 'cancelled', 'expired', 'refundpending', 'refunded'].includes(result.status)) { location.reload(); return; }
      schedule();
    } catch { schedule(); }
  };
  const schedule = () => {
    if (attempts < 20 && document.visibilityState === 'visible' && timer === null) timer = setTimeout(poll, 5000);
  };
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible' && !timer) poll(); });
  schedule();
}
