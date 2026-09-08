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
  const updateOffer = (input) => {
    if (!live || input.disabled) return;
    const price = input.dataset.price;
    const currency = input.dataset.currency || 'BRL';
    const formatted = price ? new Intl.NumberFormat('pt-BR', { style: 'currency', currency }).format(Number(price)) : 'Preço sob consulta';
    live.textContent = `${formatted}. ${input.dataset.availability === 'available' ? 'Disponível.' : 'Indisponível.'}`;
  };

  offerForm.querySelectorAll('input[data-offer-id]').forEach((input) => {
    input.addEventListener('change', () => {
      updateOffer(input);
    });
  });

  const checkedOffer = offerForm.querySelector('input[data-offer-id]:checked');
  if (checkedOffer) updateOffer(checkedOffer);
}
