const detailImage = document.querySelector('#detail-image');
const detailThumbs = [...document.querySelectorAll('.detail-thumb')];
const previousImageButton = document.querySelector('[data-gallery-prev]');
const nextImageButton = document.querySelector('[data-gallery-next]');
let activeImageIndex = detailThumbs.findIndex((button) => button.classList.contains('selected'));

const showImage = (index) => {
  if (!detailImage || detailThumbs.length === 0) return;

  activeImageIndex = (index + detailThumbs.length) % detailThumbs.length;
  const activeThumb = detailThumbs[activeImageIndex];
  detailImage.src = activeThumb.dataset.image;

  detailThumbs.forEach((button, buttonIndex) => {
    const isActive = buttonIndex === activeImageIndex;
    button.classList.toggle('selected', isActive);
    button.setAttribute('aria-pressed', String(isActive));
  });
};

detailThumbs.forEach((button, index) => {
  button.addEventListener('click', () => {
    showImage(index);
  });
});

previousImageButton?.addEventListener('click', () => showImage(activeImageIndex - 1));
nextImageButton?.addEventListener('click', () => showImage(activeImageIndex + 1));

if (activeImageIndex >= 0) showImage(activeImageIndex);

const offerForm = document.querySelector('[data-offer-form]');
if (offerForm) {
  const live = offerForm.querySelector('.live-offer');
  const validationMessage = offerForm.querySelector('#offer-validation-message');
  const offerInputs = [...offerForm.querySelectorAll('input[data-offer-id]')];
  const quantityInput = offerForm.querySelector('input[name="quantity"]');
  const showValidationMessage = (message) => {
    if (!validationMessage) return;
    validationMessage.textContent = message;
    validationMessage.hidden = !message;
  };

  offerForm.addEventListener('submit', (event) => {
    const selectedOffer = offerInputs.find((input) => input.checked && !input.disabled);
    const quantity = Number(quantityInput?.value);

    if (!selectedOffer) {
      event.preventDefault();
      showValidationMessage('Selecione uma oferta para adicionar ao carrinho.');
      offerInputs.find((input) => !input.disabled)?.focus();
      return;
    }

    if (!Number.isInteger(quantity) || quantity < 1 || quantity > 10) {
      event.preventDefault();
      showValidationMessage('A quantidade deve estar entre 1 e 10 unidades.');
      quantityInput?.focus();
      return;
    }

    showValidationMessage('');
  });

  offerForm.addEventListener('input', () => {
    showValidationMessage('');
  });

  const updateOffer = (input) => {
    if (!live || input.disabled) return;
    const price = input.dataset.price;
    const currency = input.dataset.currency || 'BRL';
    const formatted = price ? new Intl.NumberFormat('pt-BR', { style: 'currency', currency }).format(Number(price)) : 'Preço sob consulta';
    live.textContent = `${formatted}. ${input.dataset.availability === 'available' ? 'Disponível.' : 'Indisponível.'}`;
  };

  offerInputs.forEach((input) => {
    input.addEventListener('change', () => {
      updateOffer(input);
    });
  });

  const checkedOffer = offerForm.querySelector('input[data-offer-id]:checked');
  if (checkedOffer) updateOffer(checkedOffer);
}
