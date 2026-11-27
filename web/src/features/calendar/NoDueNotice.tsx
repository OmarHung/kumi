import { Link, useSearchParams, type To } from 'react-router';
import { useDateViewItems } from '../../api/queries';
import type { Cycle } from '../../api/planning';
import type { CalendarDate } from '../../api/types';
import { switchViewParams, type DateViewFilters } from '../../domain/projectView';
import type { CalendarDateMode } from '../../lib/calendarDateMode';
import { projectPagePath } from '../work-items/itemLinks';
import { noDueQuery } from './calendarData';
import { useUnplacedCount } from './useUnplacedCount';
import styles from './Calendar.module.css';

interface NoDueMessageProps {
  readonly count: number;
  /** 只讀了一頁而且滿了：說「N 個以上」，不為了一個數字把整份清單讀完。 */
  readonly hasMore: boolean;
  readonly mode: CalendarDateMode;
  /** 連到表格（條件照舊再加 due=none）。 */
  readonly to: To;
  /** 'bottom'：窄螢幕放在小月曆與選的那一天的最下面。 */
  readonly placement: 'top' | 'bottom';
}

/** 「N 個項目沒有到期日，不在月曆上。」；期間排法說「沒有開始日也沒有到期日」。專案頁與「我的工作」共用。 */
export function NoDueMessage({ count, hasMore, mode, to, placement }: NoDueMessageProps) {
  const subject = hasMore ? `${String(count)} 個以上的項目` : `${String(count)} 個項目`;
  return (
    <p className={placement === 'top' ? styles.noDue : styles.noDueBottom}>
      <Link to={to}>
        {subject}
        {mode === 'span' ? '沒有開始日也沒有到期日' : '沒有到期日'}
      </Link>
      ，不在月曆上。
    </p>
  );
}

interface NoDueNoticeProps {
  readonly projectKey: string;
  readonly filters: DateViewFilters;
  readonly todayDate: CalendarDate;
  readonly cycles: readonly Cycle[];
  readonly mode: CalendarDateMode;
  readonly placement?: 'top' | 'bottom';
}

/**
 * 「N 個項目沒有到期日」（期間排法：沒有開始日也沒有到期日）：這些項目月曆上沒有位置，說有幾個，連到表格（條件照舊，加上「沒有到期日」）。
 * 依到期日排時只讀一頁，期間排法最多讀幾頁（useUnplacedCount）：數不完就說「N 個以上」，不為了一個數字把整份清單讀完。0 個時整句不出現。
 */
export function NoDueNotice({ projectKey, filters, todayDate, cycles, mode, placement = 'top' }: NoDueNoticeProps) {
  const [searchParams] = useSearchParams();
  const query = noDueQuery(filters, todayDate, cycles);
  const result = useDateViewItems(projectKey, query ?? {}, query !== null);
  const { count, hasMore } = useUnplacedCount(result, mode);
  if (query === null || count === 0) return null;
  const params = switchViewParams(searchParams, 'table');
  params.set('due', 'none');
  return <NoDueMessage count={count} hasMore={hasMore} mode={mode} placement={placement} to={{ pathname: projectPagePath(projectKey), search: `?${params.toString()}` }} />;
}
