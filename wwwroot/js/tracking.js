const GOOGLE_ADS_CONTACT_CONVERSION = 'AW-17391563568/47KdCNHtgLEcELDm-ORA';
const API_BASE_URL = window.API_BASE_URL || (window.location.hostname === 'localhost'
  ? 'http://localhost:5001'
  : 'https://morita-api-1nnj.onrender.com');
const USAGE_EVENT_ENDPOINT = `${API_BASE_URL.replace(/\/$/, '')}/v1/WebsiteUsageEvent`;
const CONTACT_CONVERSION_EVENTS = new Set(['whatsapp_catalog_click', 'whatsapp_product_click']);
const LEAD_EVENT = 'generate_lead';

function getPageCategory() {
  const body = document.body;
  if (body?.classList.contains('jiu-jitsu-page')) return 'jiu-jitsu';
  if (body?.classList.contains('muay-thai-page')) return 'muay-thai';
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

function getUtmPayload() {
  const params = new URLSearchParams(window.location.search);

  return {
    utmSource: params.get('utm_source') || undefined,
    utmMedium: params.get('utm_medium') || undefined,
    utmCampaign: params.get('utm_campaign') || undefined,
  };
}

function toWebsiteUsagePayload(eventName, payload) {
  return {
    eventName,
    eventCategory: payload.event_category,
    pagePath: payload.page_path || window.location.pathname,
    pageTitle: payload.page_title || document.title,
    destinationUrl: payload.destination_url || undefined,
    selectedCategory: payload.selected_category || undefined,
    referrer: document.referrer || undefined,
    ...getUtmPayload(),
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

function sendWebsiteUsageEvent(eventName, payload) {
  const body = JSON.stringify(toWebsiteUsagePayload(eventName, payload));

  if (navigator.sendBeacon) {
    const sent = navigator.sendBeacon(USAGE_EVENT_ENDPOINT, new Blob([body], { type: 'application/json' }));
    if (sent) return;
  }

  fetch(USAGE_EVENT_ENDPOINT, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body,
    keepalive: true,
  }).catch(() => {});
}

function sendPageViewEvent() {
  sendWebsiteUsageEvent('page_view', {
    event_category: getPageCategory(),
    page_path: window.location.pathname,
    page_title: document.title,
    destination_url: '',
    selected_category: undefined,
  });
}

sendPageViewEvent();

document.addEventListener('click', (event) => {
  if (!(event.target instanceof Element)) return;

  const trackedElement = event.target.closest('[data-track-event]');
  if (!trackedElement) return;

  const eventName = trackedElement.dataset.trackEvent;
  const payload = getEventPayload(trackedElement);

  pushDataLayerEvent(eventName, payload);
  sendGtagEvent(eventName, payload);
  sendWebsiteUsageEvent(eventName, payload);

  if (CONTACT_CONVERSION_EVENTS.has(eventName)) {
    sendContactConversion();
  }
});

document.addEventListener('morita:lead', (event) => {
  const detail = event.detail || {};
  const payload = {
    event_category: getPageCategory(),
    page_path: window.location.pathname,
    page_title: document.title,
    destination_url: '',
    selected_category: detail.productTypes || undefined,
    modality: detail.modality || undefined,
    item_count: detail.itemCount || undefined,
  };

  pushDataLayerEvent(LEAD_EVENT, payload);
  sendGtagEvent(LEAD_EVENT, payload);
  sendWebsiteUsageEvent('customer_product_request_submit', payload);
  sendContactConversion();
});
