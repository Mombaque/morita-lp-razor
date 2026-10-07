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

const lightbox = document.querySelector('[data-lightbox]');
const lightboxTrigger = document.querySelector('[data-lightbox-open]');
if (lightbox && lightboxTrigger && typeof lightbox.showModal === 'function') {
  const stage = lightbox.querySelector('[data-lightbox-stage]');
  const lightboxImage = lightbox.querySelector('[data-lightbox-image]');
  const counter = lightbox.querySelector('[data-lightbox-counter]');
  const thumbStrip = lightbox.querySelector('[data-lightbox-thumbs]');
  const lightboxPrev = lightbox.querySelector('[data-lightbox-prev]');
  const lightboxNext = lightbox.querySelector('[data-lightbox-next]');
  const zoomScale = 2;
  const tapTolerance = 10;
  const swipeThreshold = 50;
  let lightboxThumbs = [];
  let zoomed = false;
  let origin = { x: 50, y: 50 };
  let pointer = null;

  const clamp = (value) => Math.min(100, Math.max(0, value));

  const applyZoom = () => {
    lightboxImage.style.transformOrigin = `${origin.x}% ${origin.y}%`;
    lightboxImage.style.transform = zoomed ? `scale(${zoomScale})` : '';
    stage.classList.toggle('is-zoomed', zoomed);
  };

  const setZoom = (value, clientX, clientY) => {
    zoomed = value;
    if (zoomed && clientX !== undefined) {
      const rect = stage.getBoundingClientRect();
      origin = { x: clamp(((clientX - rect.left) / rect.width) * 100), y: clamp(((clientY - rect.top) / rect.height) * 100) };
    }
    if (!zoomed) origin = { x: 50, y: 50 };
    applyZoom();
  };

  const showLightboxImage = (index) => {
    showImage(index);
    lightboxImage.src = visibleDetailThumbs[activeImageIndex]?.dataset.image ?? detailImage.src;
    if (counter) counter.textContent = `${activeImageIndex + 1} / ${visibleDetailThumbs.length}`;
    lightboxThumbs.forEach((thumb, thumbIndex) => {
      const isActive = thumbIndex === activeImageIndex;
      thumb.classList.toggle('selected', isActive);
      thumb.setAttribute('aria-pressed', String(isActive));
      if (isActive) thumb.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    });
    setZoom(false);
  };

  const openLightbox = () => {
    const multiple = visibleDetailThumbs.length > 1;
    if (lightboxPrev) lightboxPrev.hidden = !multiple;
    if (lightboxNext) lightboxNext.hidden = !multiple;
    if (counter) counter.hidden = !multiple;
    lightboxThumbs = visibleDetailThumbs.map((source, index) => {
      const thumb = document.createElement('button');
      thumb.type = 'button';
      thumb.className = 'detail-thumb';
      thumb.setAttribute('aria-label', `Ver imagem ${index + 1}`);
      const image = document.createElement('img');
      image.src = source.dataset.image;
      image.alt = '';
      thumb.append(image);
      thumb.addEventListener('click', () => showLightboxImage(index));
      return thumb;
    });
    thumbStrip?.replaceChildren(...(multiple ? lightboxThumbs : []));
    document.documentElement.classList.add('lightbox-open');
    lightbox.showModal();
    showLightboxImage(activeImageIndex);
  };

  lightboxTrigger.addEventListener('click', openLightbox);
  lightbox.querySelector('[data-lightbox-close]')?.addEventListener('click', () => lightbox.close());
  lightboxPrev?.addEventListener('click', () => showLightboxImage(activeImageIndex - 1));
  lightboxNext?.addEventListener('click', () => showLightboxImage(activeImageIndex + 1));

  lightbox.addEventListener('close', () => {
    document.documentElement.classList.remove('lightbox-open');
    pointer = null;
    setZoom(false);
    lightboxTrigger.focus();
  });

  lightbox.addEventListener('keydown', (event) => {
    if (visibleDetailThumbs.length < 2) return;
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      showLightboxImage(activeImageIndex - 1);
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      showLightboxImage(activeImageIndex + 1);
    }
  });

  stage.addEventListener('pointerdown', (event) => {
    if (event.button !== 0) return;
    pointer = { id: event.pointerId, onImage: event.target === lightboxImage, startX: event.clientX, startY: event.clientY, lastX: event.clientX, lastY: event.clientY, moved: false };
    stage.setPointerCapture(event.pointerId);
  });

  stage.addEventListener('pointermove', (event) => {
    if (zoomed && event.pointerType === 'mouse') {
      setZoom(true, event.clientX, event.clientY);
      return;
    }
    if (!pointer || pointer.id !== event.pointerId) return;
    if (Math.hypot(event.clientX - pointer.startX, event.clientY - pointer.startY) > tapTolerance) pointer.moved = true;
    if (zoomed) {
      const rect = stage.getBoundingClientRect();
      const factor = 100 / (zoomScale - 1);
      origin = { x: clamp(origin.x - ((event.clientX - pointer.lastX) / rect.width) * factor), y: clamp(origin.y - ((event.clientY - pointer.lastY) / rect.height) * factor) };
      applyZoom();
    }
    pointer.lastX = event.clientX;
    pointer.lastY = event.clientY;
  });

  stage.addEventListener('pointerup', (event) => {
    if (!pointer || pointer.id !== event.pointerId) return;
    const { onImage, startX, startY, moved } = pointer;
    pointer = null;
    if (!moved) {
      if (onImage) setZoom(!zoomed, event.clientX, event.clientY);
      else if (!zoomed) lightbox.close();
      return;
    }
    const deltaX = event.clientX - startX;
    if (!zoomed && visibleDetailThumbs.length > 1 && Math.abs(deltaX) > swipeThreshold && Math.abs(deltaX) > Math.abs(event.clientY - startY))
      showLightboxImage(activeImageIndex + (deltaX < 0 ? 1 : -1));
  });

  stage.addEventListener('pointercancel', () => { pointer = null; });
}

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

    const maxQuantity = Number(quantityInput?.max) || 10;
    if (!Number.isInteger(quantity) || quantity < 1 || quantity > maxQuantity) {
      showValidationMessage(`A quantidade deve estar entre 1 e ${maxQuantity} unidades.`);
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
      if (payload?.ok && window.moritaMiniCart) {
        const price = selectedOffer.dataset.price;
        window.moritaMiniCart.open({
          name: document.querySelector('.detail-copy h1')?.textContent?.trim(),
          variant: selectedOffer.closest('label')?.getAttribute('aria-label'),
          image: detailImage?.getAttribute('src'),
          price: price ? new Intl.NumberFormat('pt-BR', { style: 'currency', currency: selectedOffer.dataset.currency || 'BRL' }).format(Number(price) * quantity) : '',
          quantity
        }, payload.cartCount);
        return;
      }
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
