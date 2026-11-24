import type { Cycle, Milestone } from '../../../api/planning';
import type { Item } from '../../../api/types';
import {
  DELETED_CYCLE_TEXT,
  DELETED_MILESTONE_TEXT,
  displayCycleName,
  displayMilestoneName,
  UNKNOWN_CYCLE_TEXT,
  UNKNOWN_MILESTONE_TEXT,
} from '../../../domain/planningFilter';
import { EmptyValue } from '../../work-items/ItemCells';
import { useProjectTable } from '../ProjectTableContext';
import { usePlanning } from '../usePlanning';
import { EditableCell, type FieldEditSpec } from './EditableCell';
import { SelectEditor, type SelectOption } from './editors';
import { patchCommand, UNCHANGED } from './useItemCommand';

/** 清單還沒載入時的字：不把還沒載入的當成已刪除。清單讀不到時用中性的說法（某個週期），不說成已刪除。 */
const LOADING_TEXT = '…';

function planningOptions(
  rows: readonly { readonly id: string; readonly name: string }[],
  emptyLabel: string,
  currentId: string | null,
  deletedLabel: string,
): SelectOption[] {
  // 項目指著已經查不到的：接在最後，選單選得到、不會看起來像沒有；選回它就是沒改，不會送出。
  const isMissing = currentId !== null && !rows.some((row) => row.id === currentId);
  return [
    { value: '', label: emptyLabel },
    ...rows.map((row) => ({ value: row.id, label: row.name })),
    ...(isMissing ? [{ value: currentId, label: deletedLabel }] : []),
  ];
}

/** missingLabel：項目指著清單裡沒有的週期時選單最後那一項；清單讀不到時是中性的說法，不說已刪除。 */
export function cycleSpec(item: Item, cycles: readonly Cycle[], missingLabel = DELETED_CYCLE_TEXT): FieldEditSpec<string> {
  const current = item.cycleId ?? '';
  return {
    label: '週期',
    errorPaths: ['cycle', 'cycleId'],
    initialDraft: () => current,
    toCommand: (draft) => (draft === current ? UNCHANGED : patchCommand({ cycleId: draft || null })),
    renderEditor: (api) => <SelectEditor api={api} options={planningOptions(cycles, '沒有週期', item.cycleId, missingLabel)} />,
  };
}

export function milestoneSpec(item: Item, milestones: readonly Milestone[], missingLabel = DELETED_MILESTONE_TEXT): FieldEditSpec<string> {
  const current = item.milestoneId ?? '';
  return {
    label: '里程碑',
    errorPaths: ['milestone', 'milestoneId'],
    initialDraft: () => current,
    toCommand: (draft) => (draft === current ? UNCHANGED : patchCommand({ milestoneId: draft || null })),
    renderEditor: (api) => (
      <SelectEditor api={api} options={planningOptions(milestones, '沒有里程碑', item.milestoneId, missingLabel)} />
    ),
  };
}

export function CycleCell({ item }: { readonly item: Item }) {
  const { can } = useProjectTable();
  const { cycles, isCyclesPending, isCyclesFailed } = usePlanning(item.projectKey);
  const text = item.cycleId === null ? '' : isCyclesPending ? LOADING_TEXT : displayCycleName(item.cycleId, isCyclesFailed ? null : cycles);
  return (
    <EditableCell item={item} text={text} canEdit={can.update} {...cycleSpec(item, cycles, isCyclesFailed ? UNKNOWN_CYCLE_TEXT : DELETED_CYCLE_TEXT)}>
      {text === '' ? <EmptyValue label="沒有週期" /> : text}
    </EditableCell>
  );
}

export function MilestoneCell({ item }: { readonly item: Item }) {
  const { can } = useProjectTable();
  const { milestones, isMilestonesPending, isMilestonesFailed } = usePlanning(item.projectKey);
  const text =
    item.milestoneId === null ? '' : isMilestonesPending ? LOADING_TEXT : displayMilestoneName(item.milestoneId, isMilestonesFailed ? null : milestones);
  return (
    <EditableCell
      item={item}
      text={text}
      canEdit={can.update}
      {...milestoneSpec(item, milestones, isMilestonesFailed ? UNKNOWN_MILESTONE_TEXT : DELETED_MILESTONE_TEXT)}
    >
      {text === '' ? <EmptyValue label="沒有里程碑" /> : text}
    </EditableCell>
  );
}
