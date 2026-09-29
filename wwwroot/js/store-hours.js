const WEEKDAY_NAMES = ['domingo', 'segunda', 'terça', 'quarta', 'quinta', 'sexta', 'sábado'];
const WEEKDAY_INDEX = { Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 };
const REFRESH_INTERVAL_MS = 60 * 1000;

function getStoreClock(timeZone, now = new Date()) {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-US', { timeZone, weekday: 'short', hour: 'numeric', minute: 'numeric', hourCycle: 'h23' })
      .formatToParts(now)
      .map(part => [part.type, part.value])
  );

  return {
    weekday: WEEKDAY_INDEX[parts.weekday],
    minutes: Number(parts.hour) * 60 + Number(parts.minute),
  };
}

function formatHour(hour) {
  return `${hour}h`;
}

export function describeStoreStatus(hours, clock) {
  const today = hours[clock.weekday];
  if (today && clock.minutes >= today[0] * 60 && clock.minutes < today[1] * 60) {
    return { open: true, text: `Aberto agora · fecha às ${formatHour(today[1])}` };
  }

  if (today && clock.minutes < today[0] * 60) {
    return { open: false, text: `Fechado · abre hoje às ${formatHour(today[0])}` };
  }

  for (let offset = 1; offset <= 7; offset += 1) {
    const weekday = (clock.weekday + offset) % 7;
    const next = hours[weekday];
    if (!next) continue;
    const dayLabel = offset === 1 ? 'amanhã' : WEEKDAY_NAMES[weekday];
    return { open: false, text: `Fechado · abre ${dayLabel} às ${formatHour(next[0])}` };
  }

  return { open: false, text: 'Fechado' };
}

function renderStatus(element) {
  try {
    const hours = JSON.parse(element.dataset.hours || '{}');
    const status = describeStoreStatus(hours, getStoreClock(element.dataset.timeZone || 'America/Sao_Paulo'));
    element.textContent = status.text;
    element.classList.toggle('is-open', status.open);
    element.hidden = false;
  } catch {
    element.hidden = true;
  }
}

function init() {
  const elements = document.querySelectorAll('[data-open-status]');
  if (elements.length === 0) return;
  elements.forEach(renderStatus);
  setInterval(() => elements.forEach(renderStatus), REFRESH_INTERVAL_MS);
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
else init();
