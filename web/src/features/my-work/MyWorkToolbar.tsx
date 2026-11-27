import type { Ref } from 'react';
import { Button } from '../../components/Button/Button';
import { useSearchParams } from 'react-router';
import { useColumnWidthState, type ColumnWidthStore } from '../../components/Table/columnWidths';
import { Toolbar } from '../../components/Toolbar/Toolbar';
import { myWorkMineOf, type MyWorkFilters, type MyWorkLayout } from '../../domain/myWorkView';
import { useIsCompact } from '../../lib/useMediaQuery';
import { MyWorkCompactFilters } from './MyWorkCompactFilters';
import { MyWorkFilterBar } from './MyWorkFilterBar';
import { MyWorkGroupingField } from './MyWorkGroupingField';
import { MyWorkViewMenu } from '../saved-views/MyWorkViewMenu';
import { MyWorkMineSwitch } from './MyWorkMineSwitch';
import { MyWorkViewSwitch } from './MyWorkViewSwitch';
import { groupingOf } from './myWorkGroups';
import type { ColumnOrderStore } from './myWorkColumnOrder';
import { MyWorkColumnOrderPicker } from './MyWorkColumnOrderPicker';
import { hasSavedWidths } from './myWorkColumns';
import { myWorkSortOf } from './myWorkSort';
import styles from './MyWork.module.css';

interface MyWorkToolbarProps {
  readonly layout: MyWorkLayout;
  readonly filters: MyWorkFilters;
  readonly onFiltersChange: (next: MyWorkFilters) => void;
  /** 「清除條件」：回到預設的條件（版面與分組留著），焦點到搜尋框（寬螢幕）或「篩選」（窄螢幕）。 */
  readonly onClear: () => void;
  readonly searchRef: Ref<HTMLInputElement>;
  /** 窄螢幕「篩選」按鈕的 id。 */
  readonly filterButtonId: string;
  /** 剛改成完成或取消、會離開清單的那一句（myWorkStatus.ts）；只有表格掛著。 */
  readonly message: string;
  readonly widthStore: ColumnWidthStore;
  /** 欄的順序（「欄的順序」與表格共用）。 */
  readonly orderStore: ColumnOrderStore;
  /** 欄寬回到預設；按鈕跟著不見，焦點交給頁面的標題（MyWorkPage）。 */
  readonly onResetWidths: () => void;
}

type TableToolbarEndProps = Pick<MyWorkToolbarProps, 'message' | 'widthStore' | 'orderStore' | 'onResetWidths'>;

/**
 * 表格靠右的：分組、「欄的順序」、「重設欄寬」（拉過欄寬才有）、一直掛著的 status。
 * 窄螢幕的分組與排序收在「篩選」裡；窄螢幕是清單，沒有欄的順序與欄寬。
 */
function TableToolbarEnd({ message, widthStore, orderStore, onResetWidths }: TableToolbarEndProps) {
  const isCompact = useIsCompact();
  const [searchParams] = useSearchParams();
  // 只看已經記住的（拖曳中的暫時值不算）：拖到一半按鈕不會冒出來。
  const hasCustomWidths = hasSavedWidths(useColumnWidthState(widthStore).saved);
  return (
    <>
      {!isCompact && <MyWorkGroupingField isToolbar />}
      {!isCompact && <MyWorkColumnOrderPicker orderStore={orderStore} showProject={groupingOf(searchParams) !== 'project'} showAssignee={myWorkMineOf(searchParams) === 'created'} />}
      {hasCustomWidths && !isCompact && (
        <Button variant="ghost" size="sm" onClick={onResetWidths}>
          重設欄寬
        </Button>
      )}
      <p role="status" className={styles.settledStatus}>
        {message}
      </p>
    </>
  );
}

/**
 * 工具列（internal/HANDOFF-my-work-views.md 5.1）：由左到右是版面、範圍（我負責的｜我建立的，只有寬螢幕；窄螢幕在頁列右邊，見 MyWorkPage）、
 * 「檢視」、篩選（寬螢幕整排、窄螢幕收進「篩選」）；
 * 右邊只有表格有：分組、「欄的順序」、「重設欄寬」、status。status 放在工具列上，訊息出現時下面的清單不會被推動。
 * 四種版面用同一個工具列，切換時版面的分頁是同一個元素（焦點與報讀軟體的位置不會跳走）。
 */
export function MyWorkToolbar(props: MyWorkToolbarProps) {
  const { layout, filters, onFiltersChange, onClear, searchRef, filterButtonId } = props;
  const isCompact = useIsCompact();
  const isTable = layout === 'table';
  const [searchParams] = useSearchParams();
  return (
    <Toolbar isScrollable end={isTable ? <TableToolbarEnd {...props} /> : undefined}>
      <MyWorkViewSwitch layout={layout} />
      {!isCompact && <MyWorkMineSwitch />}
      <MyWorkViewMenu current={{ layout, filters, grouping: groupingOf(searchParams), sort: myWorkSortOf(searchParams) }} />
      {isCompact ? (
        <MyWorkCompactFilters filters={filters} onChange={onFiltersChange} onClear={onClear} buttonId={filterButtonId} isTable={isTable} />
      ) : (
        <MyWorkFilterBar filters={filters} onChange={onFiltersChange} onClear={onClear} searchRef={searchRef} />
      )}
    </Toolbar>
  );
}
