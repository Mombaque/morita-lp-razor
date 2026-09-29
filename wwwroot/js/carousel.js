(function () {
  const SWIPE_THRESHOLD_PX = 40;
  const AUTOPLAY_INTERVAL_MS = 5000;
  const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  function initCarousel(root) {
    const slides = [...root.querySelectorAll(root.dataset.carouselSlide || '.carousel-img')];
    if (slides.length < 2) return;

    let index = Math.max(0, slides.findIndex(slide => slide.classList.contains('active')));
    let autoplayTimer = null;
    let swiped = false;

    const dots = document.createElement('div');
    dots.className = 'carousel-dots';
    const dotButtons = slides.map((_, dotIndex) => {
      const dot = document.createElement('button');
      dot.type = 'button';
      dot.setAttribute('aria-label', `Mostrar imagem ${dotIndex + 1} de ${slides.length}`);
      dot.addEventListener('click', () => show(dotIndex));
      dots.appendChild(dot);
      return dot;
    });
    root.appendChild(dots);

    function show(nextIndex) {
      slides[index].classList.remove('active');
      index = (nextIndex + slides.length) % slides.length;
      slides[index].classList.add('active');
      dotButtons.forEach((dot, dotIndex) => {
        if (dotIndex === index) dot.setAttribute('aria-current', 'true');
        else dot.removeAttribute('aria-current');
      });
    }

    root.querySelector('[data-carousel-prev]')?.addEventListener('click', () => show(index - 1));
    root.querySelector('[data-carousel-next]')?.addEventListener('click', () => show(index + 1));

    let touchStartX = null;
    let touchStartY = null;
    root.addEventListener('touchstart', (event) => {
      const touch = event.touches?.[0];
      touchStartX = touch ? touch.clientX : null;
      touchStartY = touch ? touch.clientY : null;
    }, { passive: true });
    root.addEventListener('touchend', (event) => {
      const touch = event.changedTouches?.[0];
      if (touchStartX === null || !touch) return;
      const deltaX = touch.clientX - touchStartX;
      const deltaY = touch.clientY - touchStartY;
      touchStartX = null;
      if (Math.abs(deltaX) < SWIPE_THRESHOLD_PX || Math.abs(deltaX) < Math.abs(deltaY)) return;
      swiped = true;
      show(deltaX < 0 ? index + 1 : index - 1);
      stopAutoplay();
    });
    // A swipe over a linked slide must not also follow the link.
    root.addEventListener('click', (event) => {
      if (!swiped) return;
      swiped = false;
      event.preventDefault();
    }, true);

    function startAutoplay() {
      if (!('carouselAutoplay' in root.dataset) || prefersReducedMotion || autoplayTimer) return;
      autoplayTimer = setInterval(() => {
        if (!document.hidden) show(index + 1);
      }, AUTOPLAY_INTERVAL_MS);
    }

    function stopAutoplay() {
      clearInterval(autoplayTimer);
      autoplayTimer = null;
    }

    root.addEventListener('mouseenter', stopAutoplay);
    root.addEventListener('mouseleave', startAutoplay);
    root.addEventListener('focusin', stopAutoplay);
    root.addEventListener('focusout', startAutoplay);

    show(index);
    startAutoplay();
  }

  function init() {
    document.querySelectorAll('[data-carousel]').forEach(initCarousel);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
