const productOffers = [...document.querySelectorAll('[data-product-offer]')];

const formatPrice = (value, currency) => {
  if (!value) return null;
  try {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: currency || 'BRL' }).format(Number(value));
  } catch {
    return Number(value).toFixed(2);
  }
};

const selectCardImage = (card, image) => {
  if (!image) return;
  const images = [...card.querySelectorAll('.carousel-img')];
  const normalizedImage = new URL(image, window.location.href).href;
  const selected = images.find((candidate) => {
    const candidateImage = new URL(candidate.dataset.imageSource || candidate.src, window.location.href).href;
    return candidateImage === normalizedImage;
  });
  if (!selected) return;
  images.forEach((candidate) => candidate.classList.toggle('active', candidate === selected));
};

productOffers.forEach((offer) => {
  offer.addEventListener('click', (event) => {
    event.preventDefault();
    const card = offer.closest('.product');
    if (!card) return;

    card.querySelectorAll('[data-product-offer]').forEach((candidate) => {
      candidate.classList.toggle('selected', candidate === offer);
      candidate.setAttribute('aria-pressed', String(candidate === offer));
    });

    const price = formatPrice(offer.dataset.price, offer.dataset.currency);
    const priceElement = card.querySelector('.product-price');
    if (priceElement && price) priceElement.textContent = price;
    selectCardImage(card, offer.dataset.image);

    const productLink = card.querySelector('[data-product-link]');
    if (productLink) {
      const href = new URL(productLink.dataset.baseHref || productLink.href, window.location.href);
      href.searchParams.set('publicOfferId', offer.dataset.offerId);
      productLink.href = `${href.pathname}${href.search}${href.hash}`;
    }
  });
});
