const detailImage = document.querySelector('#detail-image');

document.querySelectorAll('.detail-thumb').forEach((button) => {
  button.addEventListener('click', () => {
    if (detailImage) detailImage.src = button.dataset.image;
    document.querySelectorAll('.detail-thumb').forEach((item) => item.classList.remove('selected'));
    button.classList.add('selected');
  });
});

const offerForm = document.querySelector('[data-offer-form]');
if (offerForm) {
  const live = offerForm.querySelector('.live-offer');
  offerForm.querySelectorAll('input[data-offer-id]').forEach((input) => {
    input.addEventListener('change', () => {
      const price = input.dataset.price;
      const currency = input.dataset.currency || 'BRL';
      const formatted = price ? new Intl.NumberFormat('pt-BR', { style: 'currency', currency }).format(Number(price)) : 'Preço sob consulta';
      live.textContent = `${formatted}. ${input.dataset.availability === 'available' ? 'Disponível.' : 'Indisponível.'}`;
    });
  });
}
