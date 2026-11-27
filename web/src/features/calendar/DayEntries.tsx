import { Link } from 'react-router';
import { projectPagePath } from '../work-items/itemLinks';
import { useCalendarScope } from './calendarScope';
import { CalendarItem } from './CalendarItem';
import type { DayRow } from './dayRows';
import styles from './Calendar.module.css';

/** 里程碑連到表格、篩這個里程碑（?milestone=）：看這個里程碑底下有哪些項目。 */
export function MilestoneMarker({
  projectKey,
  id,
  name,
  hasSymbol = true,
}: {
  readonly projectKey: string | null;
  readonly id: string;
  readonly name: string;
  /** 前面的 ◆：小月曆的選定日自己畫空心點，不重複。 */
  readonly hasSymbol?: boolean;
}) {
  if (projectKey === null) return <span className={styles.marker}>{name}</span>;
  const search = new URLSearchParams([['milestone', id]]).toString();
  return (
    <Link to={{ pathname: projectPagePath(projectKey), search: `?${search}` }} className={styles.marker}>
      {hasSymbol && <span aria-hidden="true">◆ </span>}
      {name}
    </Link>
  );
}

interface DayRowsProps {
  readonly rows: readonly DayRow[];
  /** 月曆的格子很窄：項目只顯示標題，編號放在名稱與 title。浮層寬，照舊顯示編號。 */
  readonly isTitleOnly?: boolean;
}

/** 一天的行：里程碑（最上面）與週期的開始、結束（字是淡的），再來是項目。文字本身就說明了意思，不靠顏色。 */
export function DayRows({ rows, isTitleOnly = false }: DayRowsProps) {
  const { projectKey } = useCalendarScope();
  return rows.map((row) => (
    <li key={row.key}>
      {row.kind === 'milestone' && <MilestoneMarker projectKey={projectKey} id={row.milestone.id} name={row.milestone.name} />}
      {row.kind === 'cycleStart' && <span className={styles.marker}>{row.cycle.name} 開始</span>}
      {row.kind === 'cycleEnd' && <span className={styles.marker}>{row.cycle.name} 結束</span>}
      {row.kind === 'item' && <CalendarItem item={row.item} isTitleOnly={isTitleOnly} range={row.range ?? null} />}
    </li>
  ));
}
