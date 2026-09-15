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
