import { useRef, useState, type SetStateAction, type SubmitEvent } from 'react';
import { queryKeys } from '../../api/queryKeys';
import type { ProjectDetail } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { findItemType } from '../../domain/blueprint';
import { toItemQuery, type ItemFilters } from '../../domain/itemFilters';
import { assigneeFromFilter, defaultItemType, fieldDefaultsFromFilters, itemDefaultsFromFilters, matchesTableFilters } from './createDefaults';
import { ITEMS_PAGE_SIZE } from './ItemResults';
import { usePlanning } from './usePlanning';
import { useProjectTable } from './ProjectTableContext';
import { useCreateItem, type CreateItemDraft, type CreateItemField } from './useCreateItem';
import { useWeekStart } from '../../lib/weekStart';

/**
 * 快速新增打到一半的內容（類型、上層、標題）與窄螢幕的「新增項目」對話框開著沒有。
 * 放在不隨寬窄換掉的那一層（useItemTableState）：視窗在寬窄之間切換時，表格上方那一列與對話框換來換去，
 * 打的字不會不見、開著的對話框也不會被關掉（docs/DESIGN.md 8.1 第 4 點）。
 */
export interface QuickAddState {
  readonly draft: CreateItemDraft;
  readonly setDraft: (next: SetStateAction<CreateItemDraft>) => void;
  readonly isDialogOpen: boolean;
  readonly setIsDialogOpen: (isOpen: boolean) => void;
  /** 「停滯」的分界用的「現在」，和結果清單同一個，查詢鍵才對得上。 */
  readonly staleNow: Date;
}

/**
 * 類型預設是篩選的類型（只選一種、而且是這個專案有的；docs/DESIGN.md 8.1 第 7 點），否則是第一個類型。
 * 篩選的類型換了，類型跟著換（上層清掉：上層的選項看類型）；篩選沒換時，使用者自己選的類型照舊。
 */
export function useQuickAddState(detail: ProjectDetail, filters: ItemFilters, staleNow: Date): QuickAddState {
  const [draft, setDraft] = useState<CreateItemDraft>(() => ({ type: defaultItemType(detail, filters.type), parentKey: '', title: '' }));
  const [followedType, setFollowedType] = useState(filters.type);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  if (followedType !== filters.type) {
    setFollowedType(filters.type);
    // 清掉篩選、或網址上是不認得的類型時不動：使用者正在用的類型照舊。
    const next = defaultItemType(detail, filters.type);
    if (next === filters.type && next !== draft.type) {
      setDraft({ ...draft, type: next, parentKey: '' });
    }
  }
  return { draft, setDraft, isDialogOpen, setIsDialogOpen, staleNow };
}

/**
 * 表格上方的快速新增與窄螢幕的「新增項目」對話框共用：類型（多於一種才要選）、上層項目（類型有限制才出現）、標題。
 * 新增的項目放在目前這份清單的最上面；成功後標題清空、焦點留在標題框，可以接著打下一個。
 * 負責人帶篩選的負責人（「我」或某個成員），優先度、「今天到期」的到期日、篩的週期與里程碑也一樣；新的項目不符合目前的條件時，「已新增」的訊息多說一句。
 * 草稿由 useQuickAddState 保管；送出的狀態（送出中、錯誤、「已新增」）跟著畫面上的那一個表單。
 */
export function useQuickAdd(filters: ItemFilters, state: QuickAddState) {
  const { detail, todayDate } = useProjectTable();
  const me = useCurrentUser().personId;
  const { cycles, milestones } = usePlanning(detail.project.key);
  const weekStart = useWeekStart();
  const { itemTypes } = detail.blueprint;
  const { draft, setDraft } = state;
  const titleRef = useRef<HTMLInputElement>(null);
  const parentTypes = findItemType(detail.blueprint, draft.type)?.parents ?? [];
  const hasTypes = itemTypes.length > 1;
  const hasParent = parentTypes.length > 0;
  const fields: CreateItemField[] = [...(hasTypes ? (['type'] as const) : []), ...(hasParent ? (['parent'] as const) : []), 'title'];
  const listKey = queryKeys.items(detail.project.key, toItemQuery(filters, ITEMS_PAGE_SIZE, todayDate, weekStart, cycles, state.staleNow));
  const create = useCreateItem({
    listKey,
    position: 'first',
    fields,
    titleRef,
    assigneeId: assigneeFromFilter(detail, filters.assignee, me),
    defaults: {
      ...itemDefaultsFromFilters(filters, todayDate, cycles, milestones),
      fieldsFor: (type) => fieldDefaultsFromFilters(detail.blueprint, filters.fields, type, me, detail.members),
    },
    matches: (item) => matchesTableFilters(item, filters, me, todayDate, weekStart, cycles, state.staleNow),
  });

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    create.submit(draft, () => {
      setDraft((current) => ({ ...current, title: '' }));
    });
  }

  return { itemTypes, parentTypes, hasTypes, hasParent, draft, setDraft, titleRef, create, handleSubmit };
}

export type QuickAdd = ReturnType<typeof useQuickAdd>;
