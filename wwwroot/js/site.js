// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

document.addEventListener('click', (event) => {
  const toggle = event.target.closest('.nav-menu-toggle');
  const openItem = event.target.closest('.nav-menu-item.is-open');

  if (toggle) {
    const item = toggle.closest('.nav-menu-item');
    const willOpen = !item.classList.contains('is-open');

    document.querySelectorAll('.nav-menu-item.is-open').forEach((otherItem) => {
      otherItem.classList.remove('is-open');
      otherItem.querySelector('.nav-menu-toggle')?.setAttribute('aria-expanded', 'false');
    });

    item.classList.toggle('is-open', willOpen);
    toggle.setAttribute('aria-expanded', String(willOpen));
    return;
  }

  if (!openItem) {
    document.querySelectorAll('.nav-menu-item.is-open').forEach((item) => {
      item.classList.remove('is-open');
      item.querySelector('.nav-menu-toggle')?.setAttribute('aria-expanded', 'false');
    });
  }
});

document.addEventListener('keydown', (event) => {
  if (event.key !== 'Escape') {
    return;
  }

  document.querySelectorAll('.nav-menu-item.is-open').forEach((item) => {
    item.classList.remove('is-open');
    item.querySelector('.nav-menu-toggle')?.setAttribute('aria-expanded', 'false');
  });
});

// Phone header: the main navigation stays collapsed behind the menu button.
const siteMenuToggle = document.querySelector('[data-site-menu-toggle]');
const siteNav = document.getElementById('site-nav');
if (siteMenuToggle && siteNav) {
  const setSiteMenu = (open) => {
    siteMenuToggle.setAttribute('aria-expanded', String(open));
    siteMenuToggle.setAttribute('aria-label', open ? 'Fechar menu' : 'Abrir menu');
    document.documentElement.classList.toggle('site-menu-open', open);
  };
  siteMenuToggle.addEventListener('click', () => setSiteMenu(siteMenuToggle.getAttribute('aria-expanded') !== 'true'));
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && siteMenuToggle.getAttribute('aria-expanded') === 'true') {
      setSiteMenu(false);
      siteMenuToggle.focus();
    }
  });
}
