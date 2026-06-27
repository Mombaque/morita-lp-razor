const GOOGLE_ADS_CONTACT_CONVERSION = 'AW-17391563568/47KdCNHtgLEcELDm-ORA';

function getPageCategory() {
  const path = window.location.pathname;

  if (path.includes('/jiu-jitsu')) return 'jiu-jitsu';
  if (path.includes('/muay-thai')) return 'muay-thai';
  return 'home';
}

function getEventPayload(element) {
  return {
    event_category: element.dataset.trackCategory || getPageCategory(),
    page_path: window.location.pathname,
    page_title: document.title,
    destination_url: element.href || '',
    selected_category: element.dataset.trackSelectedCategory || undefined,
  };
}

function pushDataLayerEvent(eventName, payload) {
  window.dataLayer = window.dataLayer || [];
  window.dataLayer.push({
    event: eventName,
    ...payload,
  });
}

function sendGtagEvent(eventName, payload) {
  if (typeof window.gtag !== 'function') return;

  window.gtag('event', eventName, payload);
}

function sendContactConversion() {
  if (typeof window.gtag !== 'function') return;

  window.gtag('event', 'conversion', {
    send_to: GOOGLE_ADS_CONTACT_CONVERSION,
  });
}

document.addEventListener('click', (event) => {
  if (!(event.target instanceof Element)) return;

  const trackedElement = event.target.closest('[data-track-event]');
  if (!trackedElement) return;

  const eventName = trackedElement.dataset.trackEvent;
  const payload = getEventPayload(trackedElement);

  pushDataLayerEvent(eventName, payload);
  sendGtagEvent(eventName, payload);

  if (eventName === 'whatsapp_catalog_click') {
    sendContactConversion();
  }
});
