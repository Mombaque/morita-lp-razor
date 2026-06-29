const WHATSAPP_PHONE = '5515981079332';

const dataElement = document.getElementById('product-detail-data');
const product = dataElement ? JSON.parse(dataElement.textContent || '{}') : null;
const state = {
  selectedVariant: product?.colorVariants?.[0] || null,
  selectedSize: product?.availableSizes?.[0] || null,
  imageIndex: 0,
};

if (product) {
  document.addEventListener('click', event => {
    if (!(event.target instanceof Element)) return;

    const colorButton = event.target.closest('[data-color]');
    if (colorButton) {
      selectColor(colorButton.dataset.color);
      return;
    }

    const sizeButton = event.target.closest('[data-size]');
    if (sizeButton) {
      selectSize(sizeButton.dataset.size);
      return;
    }

    const carouselButton = event.target.closest('[data-carousel-direction]');
    if (carouselButton) {
      navigateCarousel(Number(carouselButton.dataset.carouselDirection));
    }
  });
}

function selectColor(color) {
  state.selectedVariant = product.colorVariants?.find(variant => variant.color === color) || product.colorVariants?.[0] || null;
  state.imageIndex = 0;
  renderCarousel();
  updateChoiceState('[data-color]', color);
  updatePriceAndWhatsApp();
}

function selectSize(size) {
  state.selectedSize = size;
  updateChoiceState('[data-size]', size);
  updatePriceAndWhatsApp();
}

function navigateCarousel(direction) {
  const images = getSelectedImages();
  if (!images.length) return;
  state.imageIndex = (state.imageIndex + direction + images.length) % images.length;
  renderCarousel();
}

function renderCarousel() {
  const root = document.getElementById('detail-carousel');
  const images = getSelectedImages();
  if (!root) return;

  if (!images.length) {
    root.innerHTML = '<div class="detail-image-placeholder">Imagem em breve</div>';
    return;
  }

  root.innerHTML = images.map((image, index) => '<img src="' + escapeHtml(image) + '" alt="' + escapeHtml(product.name) + '" class="detail-carousel-img ' + (index === state.imageIndex ? 'active' : '') + '">').join('') +
    (images.length > 1 ? '<button class="detail-carousel-btn prev" type="button" data-carousel-direction="-1" aria-label="Imagem anterior">&#10094;</button><button class="detail-carousel-btn next" type="button" data-carousel-direction="1" aria-label="Próxima imagem">&#10095;</button>' : '');
}

function updateChoiceState(selector, selectedValue) {
  document.querySelectorAll(selector).forEach(button => {
    button.classList.toggle('active', button.dataset.color === selectedValue || button.dataset.size === selectedValue);
  });
}

function updatePriceAndWhatsApp() {
  const price = state.selectedVariant?.displayPrice || product.displayPrice || product.formattedPrice;
  const priceElement = document.getElementById('product-detail-price');
  const whatsappElement = document.getElementById('product-whatsapp-cta');
  if (priceElement) priceElement.textContent = price;
  if (whatsappElement) whatsappElement.href = getWhatsAppUrl(price);
}

function getSelectedImages() {
  return state.selectedVariant?.images || [];
}

function getWhatsAppUrl(price) {
  const message = [
    'Olá! Tenho interesse no produto ' + product.name + '.',
    state.selectedVariant?.color ? 'Cor: ' + state.selectedVariant.color + '.' : '',
    state.selectedSize ? 'Tamanho: ' + state.selectedSize + '.' : '',
    'Preço exibido: ' + price + '.',
  ].filter(Boolean).join(' ');
  return 'https://wa.me/' + WHATSAPP_PHONE + '?text=' + encodeURIComponent(message);
}

function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}
