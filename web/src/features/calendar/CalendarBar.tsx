import { Link, useSearchParams } from 'react-router';
import type { CalendarDate } from '../../api/types';
import { buttonClassName } from '../../components/Button/Button';
import { Select } from '../../components/Select/Select';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import {
  CALENDAR_DATE_MODE_LABELS,
  CALENDAR_DATE_MODES,
  isCalendarDateMode,
  setCalendarDateMode,
  useCalendarDateMode,
} from '../../lib/calendarDateMode';
import { useIsCompact } from '../../lib/useMediaQuery';
import { setWeekStart, useWeekStart, WEEK_START_LABELS, WEEK_STARTS, type WeekStart } from '../../lib/weekStart';
import { shiftMonth, monthTitle, type MonthKey } from './calendarMonth';
import styles from './Calendar.module.css';

/** 換月份只動 month：其他參數（篩選、開著的項目面板）照舊。是今天所在的月就不寫，網址短一點、「今天」那天之後還是當月。 */
function paramsWithMonth(current: URLSearchParams, month: MonthKey, todayDate: CalendarDate): string {
  const next = new URLSearchParams(current);
  if (month === todayDate.slice(0, 7)) {
    next.delete('month');
  } else {
    next.set('month', month);
  }
  return `?${next.toString()}`;
}

const WEEK_START_OPTIONS = WEEK_STARTS.map((value) => ({ value, label: WEEK_START_LABELS[value] }));

function isWeekStart(value: string): value is WeekStart {
  return WEEK_STARTS.some((choice) => choice === value);
}

/** 一週從哪天開始：個人的顯示偏好（lib/weekStart.ts），選了就生效、記在這個瀏覽器，不進網址。 */
function WeekStartSelect() {
  const weekStart = useWeekStart();
  return (
    <Select
      size="sm"
      width="content"
      aria-label="一週從"
      value={weekStart}
      options={WEEK_START_OPTIONS}
      onChange={(value) => {
        if (isWeekStart(value)) setWeekStart(value);
      }}
    />
  );
}

const DATE_MODE_OPTIONS = CALENDAR_DATE_MODES.map((value) => ({ value, label: CALENDAR_DATE_MODE_LABELS[value] }));

/** 項目排在哪幾天：到期日那一天，或從開始日畫到到期日。個人的顯示偏好（lib/calendarDateMode.ts），記在這個瀏覽器，不進網址。 */
function DateModeSwitch() {
  const mode = useCalendarDateMode();
  return (
    <SegmentedControl
      label="排法"
      options={DATE_MODE_OPTIONS}
      value={mode}
      onChange={(value) => {
        if (isCalendarDateMode(value)) setCalendarDateMode(value);
      }}
    />
  );
}

interface CalendarBarProps {
  readonly month: MonthKey;
  readonly todayDate: CalendarDate;
}

/**
 * 內容區上方的一列：「2026 年 11 月」（h2）與「‹ 上個月」「今天」「下個月 ›」。
 * 寬螢幕換月按鈕旁多「排法」（到期日／期間）的切換與「一週從」的下拉：都是同一列的控制項，這一列本來就有按鈕，所以不會變高，
 * contentStart 的預算不受影響（1440 是 165、預算 176）；窄螢幕的議程依到期日、依日期列出，和排法、一週從哪天開始都無關，不放。
 * 月份是網址的一部分，所以切換是連結（不多一筆瀏覽紀錄：和換篩選條件一樣，上一頁回到上一個畫面，不是上一個月）。
 */
export function CalendarBar({ month, todayDate }: CalendarBarProps) {
  const [searchParams] = useSearchParams();
  // 窄螢幕三顆按鈕要排進標題同一行（觸控時每顆至少 44px 高、寬度也要夠）：文字縮短，名稱與看到的一致。
  const isCompact = useIsCompact();
  const link = (target: MonthKey) => ({ search: paramsWithMonth(searchParams, target, todayDate) });
  return (
    <div className={styles.bar}>
      <h2 className={styles.title} aria-live="polite">
        {monthTitle(month)}
      </h2>
      <div className={styles.controls}>
        {!isCompact && <DateModeSwitch />}
        {!isCompact && <WeekStartSelect />}
        <nav className={styles.nav} aria-label="切換月份">
          <Link replace to={link(shiftMonth(month, -1))} className={buttonClassName('secondary', 'sm')}>
            {isCompact ? '‹ 上月' : '‹ 上個月'}
          </Link>
          <Link replace to={link(todayDate.slice(0, 7))} className={buttonClassName('secondary', 'sm')}>
            今天
          </Link>
          <Link replace to={link(shiftMonth(month, 1))} className={buttonClassName('secondary', 'sm')}>
            {isCompact ? '下月 ›' : '下個月 ›'}
          </Link>
        </nav>
      </div>
    </div>
  );
}
