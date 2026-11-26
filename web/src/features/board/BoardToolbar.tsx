import { Button } from '../../components/Button/Button';
import { SelectField } from '../../components/Field/Field';
import { hasActiveBoardFilters, type FilteredView } from '../../domain/projectView';
import { useIsCompact } from '../../lib/useMediaQuery';
import { ConditionFields, FilterBar, FilterPopover, MoreConditions, SearchField, type ConditionKey } from '../item-table/FilterFields';
import { useInlineConditions, type CollapsibleCondition } from '../item-table/inlineConditions';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { toolbarFieldProps } from '../item-table/toolbarField';
import { BoardGroupSelect } from './BoardGroupSelect';
import type { BoardFilterState } from './useBoardFilters';

// 這個檔案由頁面的工具列（ProjectWorkspace）直接匯入：不要匯入拖曳的套件或 boardDnd，看板的那些是 lazy 載入的。

interface BoardFieldsProps {
  readonly board: BoardFilterState;
  /** 寬螢幕的工具列：欄位矮一號、標籤視覺上隱藏；窄螢幕的浮層裡標籤看得到。 */
  readonly isToolbar: boolean;
  /** 畫哪些條件：工具列上只有留在列上的（其餘在「更多條件」）；窄螢幕的浮層是這個版面全部的條件。 */
  readonly keys: ReadonlySet<ConditionKey>;
  /** 看板才有「流程」：行事曆與時間軸不分流程。預設有。 */
  readonly hasWorkflow?: boolean;
}

function WorkflowField({ board, isToolbar }: { readonly board: BoardFilterState; readonly isToolbar: boolean }) {
  const { filters, setFilters, workflows, workflow } = board;
  // 依負責人、欄位分組時不分流程（所有類型一起看），切換藏起來。
  if (workflows.length < 2 || filters.group.kind !== 'state') return null;
  return (
    <SelectField
      {...toolbarFieldProps(isToolbar)}
      label="流程"
      value={workflow?.key ?? ''}
      options={workflows.map((candidate) => ({ value: candidate.key, label: candidate.name }))}
      onChange={(key) => {
        setFilters({ ...filters, workflow: key });
      }}
    />
  );
}

/**
 * 流程（多於一個才有）、搜尋、負責人。選項的文字帶出意思：流程的名稱、「所有人」。
 * 流程是「看哪一個看板」，選了就換（規範 7.3 的馬上生效）；方向鍵與打字只移動反白，不會一路換看板。
 */
function BoardFields({ board, isToolbar, keys, hasWorkflow = true }: BoardFieldsProps) {
  const { filters, setFilters } = board;
  return (
    <>
      {hasWorkflow && <WorkflowField board={board} isToolbar={isToolbar} />}
      <SearchField
        isToolbar={isToolbar}
        inputRef={isToolbar ? board.searchRef : undefined}
        value={filters.q}
        onSearch={(q) => {
          setFilters({ ...filters, q });
        }}
      />
      <ConditionFields
        isToolbar={isToolbar}
        filters={filters}
        keys={keys}
        onChange={(patch) => {
          setFilters({ ...filters, ...patch });
        }}
      />
    </>
  );
}

/** 有條件時才有；按了跟著不見，焦點到搜尋框（窄螢幕到「篩選」，useBoardFilters 的 clearFilters）。 */
function ClearButton({ board, isToolbar }: { readonly board: BoardFilterState; readonly isToolbar: boolean }) {
  if (!hasActiveBoardFilters(board.filters)) {
    return null;
  }
  return (
    <Button variant={isToolbar ? 'ghost' : 'secondary'} size="sm" onClick={board.clearFilters}>
      清除條件
    </Button>
  );
}

/** 有幾個條件在篩選：流程是「看哪一個看板」，不算條件。 */
function activeBoardFilterCount(board: BoardFilterState): number {
  return [
    board.filters.type !== null,
    board.filters.state !== null,
    board.filters.assignee !== null,
    board.filters.createdBy !== null,
    board.filters.due !== null,
    board.filters.priority !== null,
    board.filters.cycle !== null,
    board.filters.milestone !== null,
    board.filters.q !== '',
  ].filter(Boolean).length + Object.keys(board.filters.fields).length;
}

const GROUP_LABELS: Readonly<Record<FilteredView, string>> = {
  board: '篩選看板',
  calendar: '篩選行事曆',
  timeline: '篩選時間軸',
};

/**
 * 看板在頁面工具列上的控制項（接在檢視的分頁後面，和表格同一個位置）：寬螢幕整排篩選，
 * 窄螢幕收進「篩選」（和表格同一個 FilterPopover）。行事曆與時間軸用同一套（view），只是沒有「流程」。
 */
export function BoardToolbarControls({ board, view = 'board' }: { readonly board: BoardFilterState; readonly view?: FilteredView }) {
  const isCompact = useIsCompact();
  const { detail } = useProjectTable();
  const hasWorkflow = view === 'board' && board.workflows.length > 1 && board.filters.group.kind === 'state';
  const groupLabel = GROUP_LABELS[view];
  // 可以收的條件：負責人、建立者、到期、優先度；行事曆與時間軸再加狀態與（多於一種類型時的）類型，看板依狀態分欄所以沒有。
  // 固定的：搜尋，看板多於一個流程時再加流程。週期與里程碑不算：它們一律在「更多條件」裡。
  const present: readonly CollapsibleCondition[] =
    view === 'board'
      ? ['assignee', 'createdBy', 'due', 'priority']
      : [...(detail.blueprint.itemTypes.length > 1 ? (['type'] as const) : []), 'state', 'assignee', 'createdBy', 'due', 'priority'];
  // 分組另外算：篩選全部收完還放不下，它也收進「更多條件」（inlineConditions.ts 的 groupPlacement）。
  const { barRef, inlineKeys, moreKeys, hasMore, isGroupInline } = useInlineConditions(
    { present, fixedCount: hasWorkflow ? 2 : 1, hasGroup: view === 'board' },
    board.filters,
  );
  if (isCompact) {
    return (
      <FilterPopover count={activeBoardFilterCount(board)} buttonId={board.filterButtonId} groupLabel={groupLabel}>
        <BoardFields board={board} isToolbar={false} hasWorkflow={view === 'board'} keys={new Set<ConditionKey>([...present, 'cycle', 'milestone', 'fields'])} />
        {view === 'board' && <BoardGroupSelect board={board} isToolbar={false} />}
        <ClearButton board={board} isToolbar={false} />
      </FilterPopover>
    );
  }
  return (
    <FilterBar label={groupLabel} barRef={barRef}>
      <BoardFields board={board} isToolbar hasWorkflow={view === 'board'} keys={inlineKeys} />
      {view === 'board' && isGroupInline && <BoardGroupSelect board={board} isToolbar />}
      {hasMore && (
        <MoreConditions
          filters={board.filters}
          keys={moreKeys}
          leading={view === 'board' && !isGroupInline ? <BoardGroupSelect board={board} isToolbar={false} /> : undefined}
          onChange={(patch) => {
            board.setFilters({ ...board.filters, ...patch });
          }}
        />
      )}
      <ClearButton board={board} isToolbar />
    </FilterBar>
  );
}
