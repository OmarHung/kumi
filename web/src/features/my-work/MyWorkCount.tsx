import { useQuery } from '@tanstack/react-query';
import { getMyWork } from '../../api/myWork';
import { queryKeys } from '../../api/queryKeys';
import type { Item } from '../../api/types';
import { CountBadge, CountDescription } from '../../components/CountBadge/CountBadge';
import { isOverdue, isTerminal } from '../../domain/dates';
import { latestItems, useRecordedCopies } from './useMyWorkRows';
import { useToday } from '../../lib/useToday';
import styles from './MyWorkCount.module.css';

const NO_ITEMS: readonly Item[] = [];

interface MyWorkSummary {
  readonly total: number;
  readonly overdue: number;
}

/**
 * 只讀已經載入的我的工作（enabled: false，和 useMyWork 同一個鍵）：側欄在每一頁都在，為了數量自己去讀的話，
 * 每一頁都多一個請求。還沒進過我的工作就沒有資料，不顯示數量。剛改成完成或取消的（單一項目快取裡比較新的那一份）不算。
 * 「今天」在回到分頁時重新取（useToday）：側欄一直掛著，分頁開過夜時過期的數量才對得上重新進入我的工作的分組。
 */
function useMyWorkSummary(): MyWorkSummary | null {
  const myWork = useQuery({ queryKey: queryKeys.myWork, queryFn: ({ signal }) => getMyWork(signal), enabled: false });
  const items = myWork.data?.items ?? NO_ITEMS;
  const { copies } = useRecordedCopies(items);
  const todayDate = useToday();
  if (myWork.data === undefined) return null;
  const open = latestItems(items, copies).filter((item) => !isTerminal(item.stateCategory));
  return {
    total: open.length,
    overdue: open.filter((item) => isOverdue(item.dueDate, item.stateCategory, todayDate)).length,
  };
}

/** 給報讀軟體的完整說法（「3 項」「1 項已過期」），和 NavTabs 的計數一樣由 CountDescription 用「、」接起來。 */
function describe(summary: MyWorkSummary | null): readonly string[] {
  if (summary === null || summary.total === 0) return [];
  return [`${String(summary.total)} 項`, ...(summary.overdue > 0 ? [`${String(summary.overdue)} 項已過期`] : [])];
}

/**
 * 側欄「我的工作」旁邊的數量（docs/DESIGN.md 6.1）。畫面上是和分頁的計數同一個長相的 CountBadge：數量是中性的，
 * 過期的是危險色加錯誤的圖示（不只靠顏色），都是 aria-hidden；完整的說法放在 hidden 的元素裡（CountDescription），
 * 由連結的 aria-describedby 連過來——連結的名稱維持「我的工作」。元素一直掛著（沒有數量時是空的），
 * aria-describedby 不會指到不存在的 id。
 */
export function MyWorkCount({ id }: { readonly id: string }) {
  const summary = useMyWorkSummary();
  const labels = describe(summary);
  return (
    <>
      {summary !== null && summary.total > 0 && (
        // aria-hidden 也包住 title：不然沒有文字的這一層會把 title 當成名稱的一部分。
        <span className={styles.counts} aria-hidden="true" title={labels.join('、')}>
          <CountBadge value={summary.total} />
          {summary.overdue > 0 && <CountBadge value={summary.overdue} tone="danger" />}
        </span>
      )}
      <CountDescription id={id} labels={labels} />
    </>
  );
}
