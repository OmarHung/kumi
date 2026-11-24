import { useRef } from 'react';
import type { WorkflowState } from '../../api/blueprint';
import { errorMessage } from '../../api/http';
import type { useBoardColumnItems } from '../../api/queries';
import type { Item } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { hasActiveBoardFilters } from '../../domain/projectView';
import { useItemSequence } from '../item-page/ItemPanelContext';
import { StateBadge } from '../work-items/ItemCells';
import { BoardCard, CardSkeleton } from './BoardCard';
import { useBoard } from './BoardContext';
import { QuickAddToggle, type BoardQuickAddState } from './BoardQuickAdd';
import styles from './Board.module.css';

export type ColumnResult = ReturnType<typeof useBoardColumnItems>;

function ColumnCount({ result, count }: { readonly result: ColumnResult; readonly count: number }) {
  if (result.isPending) {
    return null;
  }
  const hasMore = result.hasNextPage;
  return (
    <p className={styles.count}>
      <span aria-hidden="true">{hasMore ? `${String(count)}+` : String(count)}</span>
      <span className="visually-hidden">{`已載入 ${String(count)} 個項目${hasMore ? '，還有更多' : ''}`}</span>
    </p>
  );
}

interface ColumnHeaderProps {
  readonly state: WorkflowState;
  readonly headingId: string;
  readonly result: ColumnResult;
  readonly count: number;
  /** 拖曳中這一欄看起來走不到的原因。 */
  readonly blockedReason: string | null;
  /** 第一欄、有 item.create 才有快速新增。 */
  readonly quickAdd: BoardQuickAddState | null;
}

/**
 * 欄的標題列：狀態的形狀與名稱（StateBadge，不只靠顏色）、已載入的數量、第一欄的「新增項目」。
 * 黏在欄的捲動容器上方，欄捲到哪裡都看得到；拖曳中走不到的原因也寫在這裡。
 * h2 只放狀態名稱的文字（圖示是 aria-hidden）：標題的名稱就是狀態名稱，欄（region）的名稱也是。
 */
export function ColumnHeader({ state, headingId, result, count, blockedReason, quickAdd }: ColumnHeaderProps) {
  return (
    <header className={styles.columnHeader}>
      <div className={styles.columnTitle}>
        <h2 id={headingId} className={styles.columnName} title={state.name}>
          <StateBadge label={state.name} category={state.category} />
        </h2>
        <ColumnCount result={result} count={count} />
        {quickAdd !== null && <QuickAddToggle quickAdd={quickAdd} />}
      </div>
      {blockedReason !== null && <p className={styles.blockedNote}>{`走不到：${blockedReason}`}</p>}
    </header>
  );
}

function LoadMore({ result, stateName }: { readonly result: ColumnResult; readonly stateName: string }) {
  return (
    <>
      {result.hasNextPage && (
        <Button
          size="sm"
          variant="ghost"
          className={styles.loadMore}
          // 重抓中（例如剛移走一張）時，手上的游標可能已經對不上，等重抓完再往下讀。
          disabled={result.isFetching}
          onClick={() => {
            void result.fetchNextPage();
          }}
        >
          {result.isFetchingNextPage ? '載入中…' : '載入更多'}
          <span className="visually-hidden">{`「${stateName}」的項目`}</span>
        </Button>
      )}
      {result.isFetchNextPageError && <Notice tone="danger">{`載入更多失敗：${errorMessage(result.error)}`}</Notice>}
    </>
  );
}

interface ColumnBodyProps {
  readonly result: ColumnResult;
  readonly items: readonly Item[];
  readonly state: WorkflowState;
}

/** 欄的內容：第一次載入是卡片形狀的骨架；第一次就讀不到是錯誤與「重試」；之後是卡片與「載入更多」。 */
export function ColumnBody({ result, items, state }: ColumnBodyProps) {
  const { pending, filters } = useBoard();
  // 項目面板的上一個／下一個在同一欄裡走（照這一欄現在的順序）；關上面板時焦點回到這一欄的那張卡片。
  const cardsRef = useRef<HTMLUListElement>(null);
  useItemSequence(items.map((item) => item.key), cardsRef);
  if (result.isPending) {
    return <CardSkeleton />;
  }
  if (result.data === undefined) {
    return <ErrorState error={result.error} title="這一欄載入失敗" onRetry={() => { void result.refetch(); }} />;
  }
  const empty = hasActiveBoardFilters(filters) ? '沒有符合條件的項目' : '這一欄沒有項目';
  return (
    <>
      <RefetchErrorNotice query={result} message="無法更新這一欄的最新資料，畫面上可能是舊的。" />
      {items.length === 0 ? (
        <p className={styles.empty}>{empty}</p>
      ) : (
        <ul ref={cardsRef} className={styles.cards}>
          {items.map((item, index) => (
            // 每一欄的第一張卡片都標上 data-content-start：scripts/ui-audit.js 取文件裡的第一個，
            // 第一欄是空的也量得到「看板的第一張卡片」（docs/DESIGN.md 6.2）。
            <li key={item.key} data-content-start={index === 0 || undefined}>
              <BoardCard item={item} isPending={pending.some((move) => move.item.key === item.key)} />
            </li>
          ))}
        </ul>
      )}
      <LoadMore result={result} stateName={state.name} />
    </>
  );
}
