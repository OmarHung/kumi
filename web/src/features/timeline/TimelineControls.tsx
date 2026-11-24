import { Link, useSearchParams } from 'react-router';
import type { CalendarDate } from '../../api/types';
import { buttonClassName } from '../../components/Button/Button';
import { NavTabs } from '../../components/NavTabs/NavTabs';
import { addDays, formatDate } from '../../domain/dates';
import { useWeekStart } from '../../lib/weekStart';
import { defaultFrom, formatRangeIn, timelineParams, type TimelinePosition, type TimelineScale, type TimelineWindow } from './timelineRange';
import styles from './Timeline.module.css';

const SCALES: readonly { readonly scale: TimelineScale; readonly label: string }[] = [
  { scale: 'week', label: '週' },
  { scale: 'month', label: '月' },
];

interface TimelineControlsProps {
  readonly window: TimelineWindow;
  readonly todayDate: CalendarDate;
  /** 「往前」「往後」一次移動幾天（stepDaysFor：看得到幾天決定）。 */
  readonly stepDays: number;
  /** corner：寬螢幕黏住的左上角（往前、今天、往後加刻度）；bar：窄螢幕清單上方的一列（沒有刻度，加範圍）。 */
  readonly layout: 'corner' | 'bar';
}

/**
 * 控制項：往前、今天、往後與刻度。看畫面的哪一段（往前、往後、今天）是換掉目前的網址：
 * 上一頁應該回到上一個畫面，不是上一段時間（和換篩選條件一樣）；刻度是另一種版面，和「表格、看板」一樣新增一筆。
 * 寬窄螢幕共用同一組往前、今天、往後：窄螢幕沒有時間軸，但清單照樣依畫面這一段讀，要換得了、也要看得到是哪一段。
 */
export function TimelineControls({ window, todayDate, stepDays, layout }: TimelineControlsProps) {
  const [searchParams] = useSearchParams();
  const weekStart = useWeekStart();
  const to = (next: TimelinePosition) => ({ search: `?${timelineParams(searchParams, next, todayDate, weekStart).toString()}` });
  const nav = (
    <div className={styles.nav}>
      <Link replace to={to({ scale: window.scale, from: addDays(window.from, -stepDays) })} className={buttonClassName('secondary', 'sm')}>
        <span aria-hidden="true">‹ </span>往前
      </Link>
      <Link replace to={to({ scale: window.scale, from: defaultFrom(todayDate, weekStart) })} className={buttonClassName('secondary', 'sm')}>
        今天
      </Link>
      <Link replace to={to({ scale: window.scale, from: addDays(window.from, stepDays) })} className={buttonClassName('secondary', 'sm')}>
        往後<span aria-hidden="true"> ›</span>
      </Link>
    </div>
  );
  if (layout === 'bar') {
    return (
      <div className={styles.compactBar}>
        <div className={styles.compactRow}>
          {nav}
          <span className={styles.range} aria-hidden="true">
            {formatRangeIn(window, window.from, window.to)}
          </span>
        </div>
      </div>
    );
  }
  const tabs = SCALES.map(({ scale, label }) => ({
    key: scale,
    label,
    to: to({ scale, from: window.from }),
    isCurrent: scale === window.scale,
  }));
  return (
    <>
      {nav}
      <div className={styles.scaleRow}>
        <NavTabs label="刻度" tabs={tabs} />
        <span className={styles.range} aria-hidden="true">{`${formatDate(window.from)}～${formatDate(window.to)}`}</span>
      </div>
    </>
  );
}
