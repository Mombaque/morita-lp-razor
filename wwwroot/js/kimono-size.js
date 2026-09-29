// Approximate kimono size charts used for suggestions only. Brands vary, so the
// store always confirms the final size with the customer on WhatsApp.
// Each row is the upper bound (inclusive) for that size.
export const ADULT_KIMONO_SIZE_CHART = [
  { size: 'A0', maxHeightCm: 160, maxWeightKg: 55 },
  { size: 'A1', maxHeightCm: 170, maxWeightKg: 70 },
  { size: 'A2', maxHeightCm: 180, maxWeightKg: 85 },
  { size: 'A3', maxHeightCm: 188, maxWeightKg: 100 },
  { size: 'A4', maxHeightCm: 195, maxWeightKg: 115 },
  { size: 'A5', maxHeightCm: 200, maxWeightKg: 125 },
  { size: 'A6', maxHeightCm: Infinity, maxWeightKg: Infinity },
];

export const KIDS_KIMONO_SIZE_CHART = [
  { size: 'M000', maxHeightCm: 100, maxWeightKg: 16 },
  { size: 'M00', maxHeightCm: 110, maxWeightKg: 20 },
  { size: 'M0', maxHeightCm: 120, maxWeightKg: 25 },
  { size: 'M1', maxHeightCm: 130, maxWeightKg: 30 },
  { size: 'M2', maxHeightCm: 140, maxWeightKg: 37 },
  { size: 'M3', maxHeightCm: 150, maxWeightKg: 45 },
  { size: 'M4', maxHeightCm: Infinity, maxWeightKg: Infinity },
];

function toPositiveNumber(value) {
  const number = Number(String(value ?? '').replace(',', '.'));
  return Number.isFinite(number) && number > 0 ? number : null;
}

function findRowIndex(chart, value, key) {
  if (value === null) return -1;
  return chart.findIndex(row => value <= row[key]);
}

// Picks the larger of the height-based and weight-based sizes, since a kimono
// that is slightly big fits better than one that is tight (cotton shrinks).
export function suggestKimonoSize(chart, heightCm, weightKg) {
  const height = toPositiveNumber(heightCm);
  const weight = toPositiveNumber(weightKg);
  if (height === null && weight === null) return null;

  const index = Math.max(
    findRowIndex(chart, height, 'maxHeightCm'),
    findRowIndex(chart, weight, 'maxWeightKg'),
  );

  return index >= 0 ? chart[index].size : null;
}
