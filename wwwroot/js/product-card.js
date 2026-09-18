const productColors = [...document.querySelectorAll('[data-product-color]')];

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

const selectCardVariant = (card, variantKey) => {
  card.querySelectorAll('[data-product-color]').forEach((candidate) => {
    const selected = candidate.dataset.variantKey === variantKey;
    candidate.classList.toggle('selected', selected);
    candidate.setAttribute('aria-pressed', String(selected));
  });

  card.querySelectorAll('[data-variant-row]').forEach((row) => {
    const selected = row.dataset.variantRow === variantKey;
    row.hidden = !selected;
  });
};

const updateProductLink = (card, offerId) => {
  const productLink = card.querySelector('[data-product-link]');
  if (!productLink || !offerId) return;

  const href = new URL(productLink.dataset.baseHref || productLink.href, window.location.href);
  href.searchParams.set('publicOfferId', offerId);
  productLink.href = `${href.pathname}${href.search}${href.hash}`;
};

const selectCardColor = (card, color) => {
  selectCardVariant(card, color.dataset.variantKey);

  const price = formatPrice(color.dataset.price, color.dataset.currency);
  const priceElement = card.querySelector('.product-price');
  if (priceElement && price) priceElement.textContent = price;
  selectCardImage(card, color.dataset.image);
  updateProductLink(card, color.dataset.offerId);
};

productColors.forEach((color) => {
  color.addEventListener('click', (event) => {
    event.preventDefault();
    const card = color.closest('.product');
    if (!card) return;

    selectCardColor(card, color);
  });
});
