import type { RefObject } from 'react';
import type { CalendarDate, Item } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { EmptyState, ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import type { ColumnWidthStore } from '../../components/Table/columnWidths';
import type { RovingGridHandle } from '../../lib/useRovingGrid';
import type { MyWorkMine } from '../../domain/myWorkView';
import { focusLastPage, LoadMore } from '../item-table/LoadMore';
import { MyWorkEmpty } from './MyWorkEmpty';
import type { ColumnOrderStore } from './myWorkColumnOrder';
import { MyWorkTable } from './MyWorkTable';
import type { useMyWorkItems } from './useMyWorkItems';
import type { MyWorkRow } from './useMyWorkRows';

/** 第一次載入時骨架的列數。 */
const SKELETON_ROWS = 6;

/** 有條件、而且沒有符合的項目：說換個條件，給「清除條件」（和專案的表格一樣）。 */
function NoMatches({ onClear }: { readonly onClear: () => void }) {
  return (
    <EmptyState title="沒有符合條件的項目" action={<Button onClick={onClear}>清除條件</Button>}>
      換個搜尋文字或篩選條件，或清除條件看全部的項目。
    </EmptyState>
  );
}

interface MyWorkResultsProps {
  readonly result: ReturnType<typeof useMyWorkItems>;
  /** 條件一定沒有結果（myWorkConditions 回 null）：不送請求，當成沒有項目。 */
  readonly isImpossible: boolean;
  /** 畫面上的列：伺服器的清單加上剛改成完成、取消而留在原位的（useMyWorkRows）。 */
  readonly rows: readonly MyWorkRow[];
  /** 伺服器給的項目（載入更多的計數與焦點用，不含留在原位的）。 */
  readonly listed: readonly Item[];
  readonly isFiltered: boolean;
  /** 看的是我負責的還是我建立的：沒有項目時的說法不同。 */
  readonly mine: MyWorkMine;
  readonly onClear: () => void;
  readonly todayDate: CalendarDate;
  readonly isLeaving: (item: Item) => boolean;
  readonly widthStore: ColumnWidthStore;
  readonly orderStore: ColumnOrderStore;
  readonly gridRef: RefObject<RovingGridHandle | null>;
}

/**
 * 表格的內容：第一次載入是骨架，只有第一次就讀不到才換成錯誤（已經有清單時重抓失敗，留著清單、多一行提示），
 * 沒有項目時兩種說法（預設的條件說這裡會出現什麼；有條件時說沒有符合的並給「清除條件」），有項目時是表格與「載入更多」。
 */
export function MyWorkResults(props: MyWorkResultsProps) {
  const { result, isImpossible } = props;
  return (
    <>
      {!isImpossible && <RefetchErrorNotice query={result} message="無法更新我的工作，清單可能是舊的。" />}
      <MyWorkBody {...props} />
    </>
  );
}

function MyWorkBody(props: MyWorkResultsProps) {
  const { result, isImpossible, rows, listed, isFiltered, mine, onClear, gridRef } = props;
  if (!isImpossible && result.isLoadingError) {
    return (
      <ErrorState
        error={result.error}
        onRetry={() => {
          void result.refetch();
        }}
      />
    );
  }
  if (!isImpossible && result.isPending) {
    // 第一次載入用骨架（一列 --row-height），表格出現時不會跳。
    return <Skeleton label="正在載入我的工作…" rows={SKELETON_ROWS} />;
  }
  if (rows.length === 0) {
    return isFiltered ? <NoMatches onClear={onClear} /> : <MyWorkEmpty mine={mine} />;
  }
  const footer = (
    <LoadMore
      result={result}
      count={listed.length}
      onButtonGone={() => {
        focusLastPage(result, listed, gridRef);
      }}
    />
  );
  return (
    <MyWorkTable
      rows={rows}
      widthStore={props.widthStore}
      orderStore={props.orderStore}
      todayDate={props.todayDate}
      isLeaving={props.isLeaving}
      footer={footer}
      gridRef={gridRef}
    />
  );
}
