const drawer = document.querySelector('[data-mini-cart]');
const backdrop = document.querySelector('[data-mini-cart-backdrop]');

if (drawer && backdrop) {
  let lastFocusedElement = null;
  const field = (name) => drawer.querySelector(`[data-mini-cart-${name}]`);
  const itemsLabel = (count) => `${count} ${count === 1 ? 'item' : 'itens'}`;

  const focusableElements = () => [...drawer.querySelectorAll('a[href], button:not([disabled])')];

  const close = () => {
    if (!drawer.classList.contains('is-open')) return;
    drawer.classList.remove('is-open');
    drawer.setAttribute('aria-hidden', 'true');
    drawer.setAttribute('inert', '');
    backdrop.classList.remove('is-visible');
    window.setTimeout(() => {
      if (!drawer.classList.contains('is-open')) backdrop.hidden = true;
    }, 240);
    lastFocusedElement?.focus();
  };

  const updateIndicator = (count) => {
    if (!Number.isInteger(count)) return;
    document.querySelectorAll('[data-cart="count"]').forEach((element) => { element.textContent = String(count); });
    document.querySelectorAll('[data-cart="indicator"]').forEach((element) => {
      element.setAttribute('aria-label', `Carrinho, ${itemsLabel(count)}`);
    });
  };

  const open = (item, count) => {
    const image = field('image');
    if (image) {
      image.hidden = !item.image;
      if (item.image) image.src = item.image;
    }
    field('name').textContent = item.name ?? '';
    field('variant').textContent = [item.variant, item.quantity > 1 ? `${item.quantity} unidades` : ''].filter(Boolean).join(' · ');
    field('price').textContent = item.price ?? '';
    field('count').textContent = Number.isInteger(count) ? `Seu carrinho tem ${itemsLabel(count)}.` : '';
    updateIndicator(count);

    lastFocusedElement = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    backdrop.hidden = false;
    drawer.classList.add('is-open');
    drawer.setAttribute('aria-hidden', 'false');
    drawer.removeAttribute('inert');
    requestAnimationFrame(() => backdrop.classList.add('is-visible'));
    drawer.focus();
  };

  drawer.querySelectorAll('[data-mini-cart-close]').forEach((button) => button.addEventListener('click', close));
  backdrop.addEventListener('click', close);
  document.addEventListener('keydown', (event) => {
    if (!drawer.classList.contains('is-open')) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      close();
      return;
    }
    if (event.key !== 'Tab') return;
    const elements = focusableElements();
    const first = elements[0];
    const last = elements[elements.length - 1];
    if (event.shiftKey && (document.activeElement === first || document.activeElement === drawer)) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });

  window.moritaMiniCart = { open, close };
}
