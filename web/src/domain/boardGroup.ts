import type { ProjectBlueprint } from '../api/blueprint';
import { FIELD_PARAM_PREFIX, isFieldKey } from './fieldFilters';

/**
 * 看板依什麼分欄。狀態是預設（一個流程一個看板，一個狀態一欄）；負責人是一個人一欄加「沒有負責人」；
 * 單選欄位是一個選項一欄加「沒有填」。拖曳卡片就是改那個值：狀態走轉換，負責人與欄位是修改項目。
 */
export type BoardGroup =
  | { readonly kind: 'state' }
  | { readonly kind: 'assignee' }
  | { readonly kind: 'field'; readonly key: string };

export const GROUP_PARAM = 'group';

export const STATE_GROUP: BoardGroup = { kind: 'state' };

/** 網址或存的檢視上的 `group`：`assignee`、`field.<key>`；沒有或不認得就是依狀態。 */
export function parseBoardGroup(value: string | null | undefined): BoardGroup {
  const text = value?.trim() ?? '';
  if (text === 'assignee') return { kind: 'assignee' };
  if (text.startsWith(FIELD_PARAM_PREFIX)) {
    const key = text.slice(FIELD_PARAM_PREFIX.length);
    if (isFieldKey(key)) return { kind: 'field', key };
  }
  return STATE_GROUP;
}

/** 寫回網址與存的檢視；依狀態是預設，不寫（網址短一點）。 */
export function boardGroupParam(group: BoardGroup): string | null {
  switch (group.kind) {
    case 'state':
      return null;
    case 'assignee':
      return 'assignee';
    case 'field':
      return `${FIELD_PARAM_PREFIX}${group.key}`;
  }
}

/** 欄位被刪了或不是單選了，就回到依狀態：網址與存的檢視可能是舊的。 */
export function knownBoardGroup(group: BoardGroup, blueprint: ProjectBlueprint): BoardGroup {
  if (group.kind !== 'field') return group;
  const field = blueprint.fields.find((candidate) => candidate.key === group.key);
  return field?.type === 'single_select' ? group : STATE_GROUP;
}

export function sameBoardGroup(a: BoardGroup, b: BoardGroup): boolean {
  return boardGroupParam(a) === boardGroupParam(b);
}
