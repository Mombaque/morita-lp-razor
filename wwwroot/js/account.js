(() => {
  const account = document.querySelector('main.account-shell');
  if (!account) return;

  const scrollKey = 'morita-account-scroll-position';

  const readScrollPosition = () => {
    try {
      const stored = sessionStorage.getItem(scrollKey);
      sessionStorage.removeItem(scrollKey);
      return stored ? JSON.parse(stored) : null;
    } catch {
      return null;
    }
  };

  account.querySelectorAll('form').forEach(form => {
    form.addEventListener('submit', () => {
      if (form.hasAttribute('data-account-reset-scroll')) {
        try {
          sessionStorage.removeItem(scrollKey);
        } catch {
          // The page remains usable when storage is unavailable.
        }
        return;
      }

      try {
        sessionStorage.setItem(scrollKey, JSON.stringify({ path: window.location.pathname, top: window.scrollY }));
      } catch {
        // The page remains usable when storage is unavailable.
      }
    });
  });

  const savedPosition = readScrollPosition();
  const errorToast = document.querySelector('[data-account-error]');
  if (savedPosition && !window.location.hash && savedPosition.path === window.location.pathname) {
    window.requestAnimationFrame(() => window.scrollTo(0, Number(savedPosition.top) || 0));
  }

  document.querySelector('[data-dismiss-account-error]')?.addEventListener('click', () => {
    errorToast?.remove();
  });

  account.querySelectorAll('[data-confirm-delete-address]').forEach(button => {
    button.addEventListener('click', event => {
      const label = button.dataset.confirmDeleteAddress || 'este endereço';
      if (!window.confirm(`Excluir o endereço ${label}?`)) event.preventDefault();
    });
  });
})();
