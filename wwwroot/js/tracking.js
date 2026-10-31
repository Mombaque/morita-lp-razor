const GOOGLE_ADS_CONTACT_CONVERSION = 'AW-17391563568/47KdCNHtgLEcELDm-ORA';
const USAGE_EVENT_ENDPOINT = '/analytics/website-usage-event';
const VISITOR_ID_KEY = 'morita-visitor-id';
const SESSION_KEY = 'morita-visit-session';
const SESSION_IDLE_TIMEOUT_MS = 30 * 60 * 1000;

function createBrowserId() {
  if (window.crypto?.randomUUID) return window.crypto.randomUUID();

  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
}

function getVisitorId() {
  try {
    const stored = window.localStorage.getItem(VISITOR_ID_KEY);
    if (stored) return stored;

    const visitorId = createBrowserId();
    window.localStorage.setItem(VISITOR_ID_KEY, visitorId);
    return visitorId;
  } catch {
    return undefined;
  }
}

function getSessionId() {
  try {
    const now = Date.now();
    const stored = JSON.parse(window.localStorage.getItem(SESSION_KEY) || 'null');
    const session = stored?.id && now - stored.lastSeenAt < SESSION_IDLE_TIMEOUT_MS
      ? stored
      : { id: createBrowserId() };

    window.localStorage.setItem(SESSION_KEY, JSON.stringify({ id: session.id, lastSeenAt: now }));
    return session.id;
  } catch {
    return undefined;
  }
}

function getExternalReferrer() {
  if (!document.referrer) return undefined;

  try {
    return new URL(document.referrer).host === window.location.host ? undefined : document.referrer;
  } catch {
    return undefined;
  }
}

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
    visitorId: getVisitorId(),
    sessionId: getSessionId(),
    eventCategory: payload.event_category,
    pagePath: payload.page_path || window.location.pathname,
    pageTitle: payload.page_title || document.title,
    destinationUrl: payload.destination_url || undefined,
    selectedCategory: payload.selected_category || undefined,
    referrer: getExternalReferrer(),
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

function purchaseAlreadyTracked(transactionId) {
  try {
    const key = `analytics-purchase:${transactionId}`;
    if (window.localStorage.getItem(key)) return true;
    window.localStorage.setItem(key, '1');
  } catch {
    return false;
  }
  return false;
}

function sendAdsPurchaseConversion(ecommerce) {
  const sendTo = document.querySelector('meta[name="analytics-ads-purchase-conversion"]')?.content;
  if (!sendTo || typeof window.gtag !== 'function') return;

  window.gtag('event', 'conversion', {
    send_to: sendTo,
    value: ecommerce.value,
    currency: ecommerce.currency,
    transaction_id: ecommerce.transaction_id,
  });
}

function trackEcommerceEvent(eventName, ecommerce) {
  if (!eventName || !ecommerce) return;
  if (eventName === 'purchase' && purchaseAlreadyTracked(ecommerce.transaction_id)) return;

  window.dataLayer = window.dataLayer || [];
  window.dataLayer.push({ ecommerce: null });
  window.dataLayer.push({ event: eventName, ecommerce });
  sendGtagEvent(eventName, ecommerce);
  if (eventName === 'purchase') sendAdsPurchaseConversion(ecommerce);
}

function trackRenderedEcommerceEvents() {
  for (const script of document.querySelectorAll('script[data-analytics-event]')) {
    try {
      const data = JSON.parse(script.textContent || '{}');
      trackEcommerceEvent(data.event, data.ecommerce);
    } catch {
      continue;
    }
  }
}

sendPageViewEvent();
window.sendWebsiteUsageEvent = sendWebsiteUsageEvent;
window.trackEcommerceEvent = trackEcommerceEvent;
trackRenderedEcommerceEvents();

document.addEventListener('click', (event) => {
  if (!(event.target instanceof Element)) return;

  const trackedElement = event.target.closest('[data-track-event]');
  if (!trackedElement) return;

  const eventName = trackedElement.dataset.trackEvent;
  const payload = getEventPayload(trackedElement);

  pushDataLayerEvent(eventName, payload);
  sendGtagEvent(eventName, payload);
  sendWebsiteUsageEvent(eventName, payload);

  if (eventName === 'whatsapp_catalog_click') {
    sendContactConversion();
  }
});
