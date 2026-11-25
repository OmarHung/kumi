const UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB'] as const;
const STEP = 1024;

/** 位元組寫成人看得懂的大小（二進位單位，和伺服器訊息裡的 GiB 一致）：1,288,490,188 → 1.2 GiB。 */
export function formatBytes(bytes: number): string {
  let value = Math.max(0, bytes);
  let unit = 0;
  while (value >= STEP && unit < UNITS.length - 1) {
    value /= STEP;
    unit += 1;
  }
  return unit === 0 ? `${String(Math.round(value))} B` : `${value.toFixed(1)} ${UNITS[unit] ?? ''}`;
}

/** 上限的寫法和伺服器訊息一致：剛好是整數的單位不帶小數（20 GiB），否則一位小數（1.5 GiB）。 */
export function formatLimit(bytes: number): string {
  let size = 1;
  let unit = 0;
  while (bytes >= size * STEP && unit < UNITS.length - 1) {
    size *= STEP;
    unit += 1;
  }
  return unit > 0 && bytes % size === 0 ? `${String(bytes / size)} ${UNITS[unit] ?? ''}` : formatBytes(bytes);
}
