const detailImage = document.querySelector('#detail-image');
const allDetailThumbs = [...document.querySelectorAll('.detail-thumb')];
const previousImageButton = document.querySelector('[data-gallery-prev]');
const nextImageButton = document.querySelector('[data-gallery-next]');
let visibleDetailThumbs = allDetailThumbs;
let activeImageIndex = 0;

const showImage = (index) => {
  if (!detailImage || visibleDetailThumbs.length === 0) return;

  activeImageIndex = (index + visibleDetailThumbs.length) % visibleDetailThumbs.length;
  const activeThumb = visibleDetailThumbs[activeImageIndex];
  detailImage.src = activeThumb.dataset.image;

  allDetailThumbs.forEach((button) => {
    const isActive = button === activeThumb;
    button.classList.toggle('selected', isActive);
    button.setAttribute('aria-pressed', String(isActive));
  });
};

const setGalleryVariant = (variantKey) => {
  const matchingThumbs = variantKey ? allDetailThumbs.filter((thumb) => thumb.dataset.variantKey === variantKey) : [];
  visibleDetailThumbs = matchingThumbs.length > 0 ? matchingThumbs : allDetailThumbs;
  allDetailThumbs.forEach((thumb) => { thumb.hidden = !visibleDetailThumbs.includes(thumb); });
  if (previousImageButton) previousImageButton.hidden = visibleDetailThumbs.length < 2;
  if (nextImageButton) nextImageButton.hidden = visibleDetailThumbs.length < 2;
  showImage(0);
};

allDetailThumbs.forEach((button) => {
  button.addEventListener('click', () => showImage(visibleDetailThumbs.indexOf(button)));
});
previousImageButton?.addEventListener('click', () => showImage(activeImageIndex - 1));
nextImageButton?.addEventListener('click', () => showImage(activeImageIndex + 1));

const detailShell = document.querySelector('[data-selected-variant-key]');
setGalleryVariant(detailShell?.dataset.selectedVariantKey || null);

const offerForm = document.querySelector('[data-offer-form]');
if (offerForm) {
  const live = offerForm.querySelector('.live-offer');
  const validationMessage = offerForm.querySelector('#offer-validation-message');
  const offerInputs = [...offerForm.querySelectorAll('input[data-offer-id]')];
  const quantityInput = offerForm.querySelector('input[name="quantity"]');
  const addButton = offerForm.querySelector('[data-cart="add"]');
  const showValidationMessage = (message) => {
    if (!validationMessage) return;
    validationMessage.textContent = message;
    validationMessage.hidden = !message;
  };

  offerForm.addEventListener('submit', async (event) => {
    event.preventDefault();
    const selectedOffer = offerInputs.find((input) => input.checked && !input.disabled);
    const quantity = Number(quantityInput?.value);

    if (!selectedOffer) {
      showValidationMessage('Selecione uma oferta para adicionar ao carrinho.');
      offerInputs.find((input) => !input.disabled)?.focus();
      return;
    }

    if (!Number.isInteger(quantity) || quantity < 1 || quantity > 10) {
      showValidationMessage('A quantidade deve estar entre 1 e 10 unidades.');
      quantityInput?.focus();
      return;
    }

    const data = new FormData(offerForm);
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    if (token && !data.get('__RequestVerificationToken'))
      data.set('__RequestVerificationToken', token);

    if (addButton) {
      addButton.disabled = true;
      addButton.setAttribute('aria-busy', 'true');
    }

    try {
      const response = await fetch(offerForm.action, {
        method: 'POST',
        body: data,
        headers: {
          Accept: 'application/json',
          'X-Requested-With': 'XMLHttpRequest'
        }
      });
      const payload = await response.json().catch(() => null);
      if (payload?.ok && payload.redirectUrl) {
        window.location.assign(payload.redirectUrl);
        return;
      }

      showValidationMessage(payload?.error || 'Não foi possível adicionar este item. Tente novamente.');
      quantityInput?.focus();
    } catch {
      showValidationMessage('Não foi possível adicionar este item. Tente novamente.');
    } finally {
      if (addButton) {
        addButton.disabled = false;
        addButton.removeAttribute('aria-busy');
      }
    }
  });

  offerForm.addEventListener('input', () => showValidationMessage(''));

  const updateOffer = (input) => {
    if (!live || input.disabled) return;
    const price = input.dataset.price;
    const currency = input.dataset.currency || 'BRL';
    const formatted = price ? new Intl.NumberFormat('pt-BR', { style: 'currency', currency }).format(Number(price)) : 'Preço sob consulta';
    const presentation = input.closest('label')?.getAttribute('aria-label') || 'Oferta selecionada';
    live.textContent = `${presentation}. ${formatted}. Disponível.`;
    const detailPrice = document.querySelector('[data-detail-price] .product-price');
    if (detailPrice) detailPrice.textContent = formatted;
    setGalleryVariant(input.dataset.variantKey);
  };

  offerInputs.forEach((input) => input.addEventListener('change', () => updateOffer(input)));

  const checkedOffer = offerForm.querySelector('input[data-offer-id]:checked:not(:disabled)');
  if (checkedOffer) updateOffer(checkedOffer);
}
