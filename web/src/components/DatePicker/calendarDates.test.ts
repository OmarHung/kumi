import { describe, expect, it } from 'vitest';
import {
  addMonths,
  dayLabel,
  monthsOfYear,
  shiftMonthsKeepingDay,
  yearLabel,
  yearPageContaining,
  yearPageStartFor,
  yearRangeLabel,
  yearsFrom,
  displayText,
  endOfWeekOf,
  isCalendarDate,
  monthGrid,
  monthLabel,
  normalizeTyped,
  parseDateInput,
  shiftDays,
  startOfWeekOf,
  WEEKDAYS,
  weekdaysFrom,
} from './calendarDates';

describe('parseDateInput：打字的格式', () => {
  it.each([
    ['2026/12/15', '2026-12-15'],
    ['2026-12-15', '2026-12-15'],
    ['2026/1/5', '2026-01-05'],
    ['2026-1-5', '2026-01-05'],
    ['2026.1.5', '2026-01-05'],
    ['20261215', '2026-12-15'],
    ['  2026/12/15 ', '2026-12-15'],
    ['２０２６／１２／１５', '2026-12-15'],
  ])('「%s」解析成 %s', (text, expected) => {
    // Act
    const parsed = parseDateInput(text);

    // Assert
    expect(parsed).toBe(expected);
  });

  it.each(['', '2026', '2026/1', '2026/13/1', '2026/2/30', '2025/2/29', '2026/0/10', 'abc', '26/12/15', '2026/12/15/1', '202612151', '0999/1/1', '09990101'])(
    '「%s」解析不了（不完整、不存在的日期、別的格式）',
    (text) => {
      // Act
      const parsed = parseDateInput(text);

      // Assert
      expect(parsed).toBeNull();
    },
  );

  it('閏年的 2 月 29 日是合法的日期', () => {
    // Act
    const leap = parseDateInput('2028/2/29');

    // Assert
    expect(leap).toBe('2028-02-29');
  });
});

describe('normalizeTyped 與 displayText：打的字和值之間', () => {
  it('空白是沒有值；解析得了是 ISO；解析不了的原封不動（呼叫端自己判斷不合法）', () => {
    // Act
    const results = ['   ', '2026/1/5', '2026/13/40'].map(normalizeTyped);

    // Assert
    expect(results).toEqual(['', '2026-01-05', '2026/13/40']);
  });

  it('值是日期時顯示成 2026/12/15；不是日期的值（打到一半的字）照原樣顯示', () => {
    // Act
    const shown = ['2026-12-15', '', '2026/13'].map(displayText);

    // Assert
    expect(shown).toEqual(['2026/12/15', '', '2026/13']);
  });

  it('isCalendarDate 只認存在的 YYYY-MM-DD，年份 1000 以上（四位數）', () => {
    // Act
    const results = ['2026-12-15', '2026-02-30', '2026/12/15', '', '0999-01-01'].map(isCalendarDate);

    // Assert
    expect(results).toEqual([true, false, false, false, false]);
  });

  it('年份小於 1000 的當成解析不了（原始文字照傳，呼叫端判斷不合法），不會變成不是 ISO 的「999-01-01」', () => {
    // Act
    const typed = normalizeTyped('0999/1/1');

    // Assert
    expect(typed).toBe('0999/1/1');
  });
});

describe('日期的加減', () => {
  it('加減天數跨月、跨年', () => {
    // Act
    const results = [shiftDays('2026-11-30', 1), shiftDays('2026-12-31', 1), shiftDays('2026-01-01', -1)];

    // Assert
    expect(results).toEqual(['2026-12-01', '2027-01-01', '2025-12-31']);
  });

  it('加減月份時日子超出那個月就停在月底（1 月 31 日的下一個月是 2 月 28 日）', () => {
    // Act
    const results = [addMonths('2026-01-31', 1), addMonths('2028-02-29', 12), addMonths('2026-03-31', -1), addMonths('2026-11-17', -12)];

    // Assert
    expect(results).toEqual(['2026-02-28', '2029-02-28', '2026-02-28', '2025-11-17']);
  });

  it('一週從週一開始：週的頭是週一、尾是週日（週日屬於前面那一週）', () => {
    // Act
    const tuesday = [startOfWeekOf('2026-11-17', 'monday'), endOfWeekOf('2026-11-17', 'monday')];
    const sunday = [startOfWeekOf('2026-11-22', 'monday'), endOfWeekOf('2026-11-22', 'monday')];

    // Assert
    expect(tuesday).toEqual(['2026-11-16', '2026-11-22']);
    expect(sunday).toEqual(['2026-11-16', '2026-11-22']);
  });

  it('一週從週日開始：週的頭是週日、尾是週六（週日屬於後面那一週）', () => {
    // Act
    const tuesday = [startOfWeekOf('2026-11-17', 'sunday'), endOfWeekOf('2026-11-17', 'sunday')];
    const sunday = [startOfWeekOf('2026-11-22', 'sunday'), endOfWeekOf('2026-11-22', 'sunday')];

    // Assert
    expect(tuesday).toEqual(['2026-11-15', '2026-11-21']);
    expect(sunday).toEqual(['2026-11-22', '2026-11-28']);
  });
});

describe('月曆', () => {
  it('六列 42 天，從 1 日所在那一週的週一開始，前後補上別的月份', () => {
    // Act
    const november = monthGrid('2026-11-17', 'monday');

    // Assert
    expect(november).toHaveLength(42);
    expect(november[0]).toBe('2026-10-26');
    expect(november[6]).toBe('2026-11-01');
    expect(november[41]).toBe('2026-12-06');
  });

  it('一週從週日開始：從 1 日所在那一週的週日排起，第一欄都是週日', () => {
    // Act
    const november = monthGrid('2026-11-17', 'sunday');

    // Assert：2026-11-01 就是週日。
    expect(november).toHaveLength(42);
    expect(november[0]).toBe('2026-11-01');
    expect(november[41]).toBe('2026-12-12');
    expect(november.filter((_, index) => index % 7 === 0).every((date) => dayLabel(date).endsWith('星期日'))).toBe(true);
  });

  it('一週從週日開始，1 日不是週日時往前補到上個月的週日', () => {
    // Act
    const december = monthGrid('2026-12-10', 'sunday');

    // Assert：2026-12-01 是週二。
    expect(december[0]).toBe('2026-11-29');
  });

  it('1 日剛好是週一時第一列從 1 日開始', () => {
    // Act
    const june = monthGrid('2026-06-10', 'monday');

    // Assert
    expect(june[0]).toBe('2026-06-01');
  });

  it('標題、每一天的名稱與星期的寫法', () => {
    // Act
    const title = monthLabel('2026-11-17');
    const day = dayLabel('2026-11-17');

    // Assert
    expect(title).toBe('2026 年 11 月');
    expect(day).toBe('2026 年 11 月 17 日 星期二');
    expect(WEEKDAYS.map((weekday) => weekday.short).join('')).toBe('一二三四五六日');
    expect(WEEKDAYS.map((weekday) => weekday.long)).toEqual(['星期一', '星期二', '星期三', '星期四', '星期五', '星期六', '星期日']);
  });

  it('表頭的順序跟著一週從哪天開始，每一格的名稱不受影響', () => {
    // Act
    const monday = weekdaysFrom('monday').map((weekday) => weekday.short).join('');
    const sunday = weekdaysFrom('sunday').map((weekday) => weekday.short).join('');

    // Assert
    expect(monday).toBe('一二三四五六日');
    expect(sunday).toBe('日一二三四五六');
    expect(dayLabel('2026-11-22')).toBe('2026 年 11 月 22 日 星期日');
  });
});

describe('月份與年份的畫面', () => {
  it('一年的 12 個月：名稱是「2026 年 12 月」、看得到的是「12 月」，鍵是 YYYY-MM', () => {
    // Act
    const months = monthsOfYear('2026-12-15');

    // Assert
    expect(months).toHaveLength(12);
    expect(months[0]).toEqual({ key: '2026-01', label: '2026 年 1 月', short: '1 月' });
    expect(months[11]).toEqual({ key: '2026-12', label: '2026 年 12 月', short: '12 月' });
  });

  it('年份的一頁是 12 年，從顯示的年份往前兩年開始（看得到明年與之後的幾年）；標題是範圍', () => {
    // Act
    const start = yearPageStartFor(2026);
    const years = yearsFrom(start);

    // Assert
    expect(start).toBe(2024);
    expect(years.map((year) => year.key)).toEqual(['2024', '2025', '2026', '2027', '2028', '2029', '2030', '2031', '2032', '2033', '2034', '2035']);
    expect(years[2]).toEqual({ key: '2026', label: '2026 年', short: '2026' });
    expect(yearRangeLabel(start)).toBe('2024～2035');
    expect(yearLabel('2026-12-15')).toBe('2026 年');
  });

  it('走出這一頁的年份時，頁面一次換 12 年（頁面不跟著每一步滑動）', () => {
    // Act
    const results = [yearPageContaining(2030, 2024), yearPageContaining(2036, 2024), yearPageContaining(2023, 2024), yearPageContaining(2049, 2024)];

    // Assert
    expect(results).toEqual([2024, 2036, 2012, 2048]);
  });

  it('在月份、年份之間移動時記著原本的日子：1 月 31 日經過 2 月到 3 月還是 31 日（2 月是 28 日）', () => {
    // Act
    const february = shiftMonthsKeepingDay('2026-01-31', 1, 31);
    const march = shiftMonthsKeepingDay(february, 1, 31);
    const leap = shiftMonthsKeepingDay('2028-02-29', 12, 29);

    // Assert
    expect(february).toBe('2026-02-28');
    expect(march).toBe('2026-03-31');
    expect(leap).toBe('2029-02-28');
  });
});
