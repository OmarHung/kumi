import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatInstant, formatRelativeTime } from './time';

const NOW = new Date(2026, 10, 15, 12, 0, 0).getTime();

function minutesAgo(minutes: number): string {
  return new Date(NOW - minutes * 60_000).toISOString();
}

describe('相對時間', () => {
  it.each([
    ['不到一分鐘是剛剛', minutesAgo(0.5), '剛剛'],
    ['未來的時間（時鐘有誤差）也是剛剛', minutesAgo(-3), '剛剛'],
    ['幾分鐘前', minutesAgo(5), '5 分鐘前'],
    ['幾小時前', minutesAgo(3 * 60), '3 小時前'],
    ['一天前是昨天', minutesAgo(24 * 60), '昨天'],
    ['幾天前', minutesAgo(3 * 24 * 60), '3 天前'],
  ])('%s', (_name, at, expected) => {
    // Act
    const text = formatRelativeTime(at, NOW);

    // Assert
    expect(text).toBe(expected);
  });

  it('一週以上直接寫日期', () => {
    // Arrange
    const at = new Date(2026, 10, 3, 9, 30).toISOString();

    // Act
    const text = formatRelativeTime(at, NOW);

    // Assert
    expect(text).toBe('2026/11/03');
  });

  it('前一年的日期也寫出年份', () => {
    // Arrange
    const at = new Date(2025, 0, 2, 9, 30).toISOString();

    // Act & Assert
    expect(formatRelativeTime(at, NOW)).toBe('2025/01/02');
  });
});

describe('相對時間的天數照使用者所在地的日曆日算', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  // 同一對時刻：台北是週一 23:00 與週三 01:00，紐約是週一 10:00 與週二 12:00；中間都是 26 小時。
  const MONDAY_NIGHT = '2026-11-16T15:00:00Z';
  const TWO_DAYS_LATER = Date.parse('2026-11-17T17:00:00Z');

  it('週一 23:00 的活動，週三 01:00 看是前天，不是用 24 小時算出來的昨天', () => {
    // Arrange
    vi.stubEnv('TZ', 'Asia/Taipei');

    // Act
    const text = formatRelativeTime(MONDAY_NIGHT, TWO_DAYS_LATER);

    // Assert
    expect(text).toBe('前天');
  });

  it('同樣的兩個時刻，在日曆上只差一天的地方是昨天', () => {
    // Arrange
    vi.stubEnv('TZ', 'America/New_York');

    // Act
    const text = formatRelativeTime(MONDAY_NIGHT, TWO_DAYS_LATER);

    // Assert
    expect(text).toBe('昨天');
  });

  it('跨過午夜但還不到一天時說幾小時前', () => {
    // Arrange
    vi.stubEnv('TZ', 'Asia/Taipei');

    // Act
    const text = formatRelativeTime(MONDAY_NIGHT, Date.parse('2026-11-16T17:00:00Z'));

    // Assert
    expect(text).toBe('2 小時前');
  });

  it('第七個日曆日起寫日期', () => {
    // Arrange
    vi.stubEnv('TZ', 'Asia/Taipei');

    // Act
    const sixDays = formatRelativeTime(MONDAY_NIGHT, Date.parse('2026-11-22T01:00:00Z'));
    const sevenDays = formatRelativeTime(MONDAY_NIGHT, Date.parse('2026-11-22T17:00:00Z'));

    // Assert
    expect(sixDays).toBe('6 天前');
    expect(sevenDays).toBe('2026/11/16');
  });
});

describe('完整時間', () => {
  it('用使用者所在地的時間，24 小時制', () => {
    // Arrange
    const at = new Date(2026, 10, 3, 9, 5).toISOString();

    // Act & Assert
    expect(formatInstant(at)).toBe('2026/11/03 09:05');
  });

  it('讀不懂的時刻原樣顯示，不讓整頁壞掉', () => {
    // Act & Assert
    expect(formatInstant('not-a-time')).toBe('not-a-time');
    expect(formatRelativeTime('not-a-time', NOW)).toBe('not-a-time');
  });
});
