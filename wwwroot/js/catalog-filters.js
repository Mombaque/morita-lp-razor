const form = document.querySelector('#catalog-filter-form');
const drawer = document.querySelector('[data-filter-drawer]');
const backdrop = document.querySelector('[data-filter-backdrop]');
const openButton = document.querySelector('[data-filter-open]');
const closeButton = document.querySelector('[data-filter-close]');
const sortControl = document.querySelector('[data-catalog-sort]');

if (form && drawer && backdrop && openButton && closeButton) {
  let lastFocusedElement = null;

  const focusableElements = () => [...drawer.querySelectorAll(
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
  )];

  const closeDrawer = (restoreHistory = true) => {
    drawer.classList.remove('is-open');
    drawer.setAttribute('aria-hidden', 'true');
    drawer.setAttribute('inert', '');
    backdrop.classList.remove('is-visible');
    document.documentElement.classList.remove('filter-drawer-open');
    document.body.classList.remove('filter-drawer-open');
    openButton.setAttribute('aria-expanded', 'false');

    window.setTimeout(() => {
      if (!drawer.classList.contains('is-open')) backdrop.hidden = true;
    }, 240);

    if (restoreHistory && history.state?.moritaCatalogFilterDrawer) history.back();
    lastFocusedElement?.focus();
  };

  const openDrawer = () => {
    if (drawer.classList.contains('is-open')) return;
    lastFocusedElement = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    backdrop.hidden = false;
    drawer.classList.add('is-open');
    drawer.setAttribute('aria-hidden', 'false');
    drawer.removeAttribute('inert');
    document.documentElement.classList.add('filter-drawer-open');
    document.body.classList.add('filter-drawer-open');
    openButton.setAttribute('aria-expanded', 'true');
    if (!history.state?.moritaCatalogFilterDrawer) {
      history.pushState({ ...(history.state ?? {}), moritaCatalogFilterDrawer: true }, '', window.location.href);
    }
    requestAnimationFrame(() => backdrop.classList.add('is-visible'));
    closeButton.focus();
  };

  openButton.addEventListener('click', openDrawer);
  closeButton.addEventListener('click', () => closeDrawer());
  backdrop.addEventListener('click', () => closeDrawer());

  window.addEventListener('popstate', () => {
    if (drawer.classList.contains('is-open')) closeDrawer(false);
  });

  document.addEventListener('keydown', (event) => {
    if (!drawer.classList.contains('is-open')) return;
    if (event.key === 'Escape') {
      event.preventDefault();
      closeDrawer();
      return;
    }
    if (event.key !== 'Tab') return;
    const elements = focusableElements();
    if (!elements.length) {
      event.preventDefault();
      drawer.focus();
      return;
    }
    const first = elements[0];
    const last = elements[elements.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });

  sortControl?.addEventListener('change', () => form.requestSubmit());
}
