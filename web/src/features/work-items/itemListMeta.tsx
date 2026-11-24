import type { ProjectBlueprint } from '../../api/blueprint';
import type { Item, ProjectDetail } from '../../api/types';
import { Avatar } from '../../components/Avatar/Avatar';
import type { ListMeta } from '../../components/List/List';
import { stateLabel } from '../../domain/blueprint';
import { DueDateText, PriorityText, StateBadge } from './ItemCells';

/*
 * 清單列（components/List）第二行的欄位，專案的項目清單、我的工作、項目頁的下層項目共用（docs/DESIGN.md 7.1）。
 * 整筆是連結時，連結的名稱就是這一筆所有的文字，所以每個欄位都帶只給報讀軟體的說法（「狀態：」「負責人：」「優先度：」「到期：」），
 * 不是只唸一串值。可有可無的欄位（優先度、到期日）回傳陣列：沒有值時是空的，畫面上不佔位置，呼叫端直接展開。
 */

/** 狀態：藍圖的狀態名稱加類別的形狀；藍圖還沒讀到時用類別的名稱。 */
export function stateMeta(item: Item, blueprint: ProjectBlueprint | undefined): ListMeta {
  const label = stateLabel(blueprint, item.type, item.state, item.stateCategory);
  return { key: 'state', label: '狀態', content: <StateBadge label={label} category={item.stateCategory} /> };
}

/** 負責人：頭像（旁邊有名字，是裝飾）加名字；沒有負責人時畫面上不佔位置，報讀軟體唸「沒有負責人」。 */
export function assigneeMeta(item: Item): ListMeta {
  if (item.assignee === null) {
    return { key: 'assignee', content: '沒有負責人', isVisuallyHidden: true };
  }
  const { displayName } = item.assignee;
  return {
    key: 'assignee',
    label: '負責人',
    content: (
      <>
        <Avatar name={displayName} size="sm" decorative />
        {displayName}
      </>
    ),
  };
}

/** 優先度：有設才有。 */
export function priorityMeta(item: Item): ListMeta[] {
  return item.priority === 'none' ? [] : [{ key: 'priority', label: '優先度', content: <PriorityText priority={item.priority} /> }];
}

/** 到期日：有設才有；過期的標法和表格一樣（完成或取消的不算）。 */
export function dueMeta(item: Item, todayDate: string): ListMeta[] {
  if (item.dueDate === null) {
    return [];
  }
  return [{ key: 'due', label: '到期', content: <DueDateText dueDate={item.dueDate} category={item.stateCategory} today={todayDate} /> }];
}

/** 專案（我的工作跨專案）：專案內容還沒讀到時用代號。 */
export function projectMeta(item: Item, project: ProjectDetail | undefined): ListMeta {
  return { key: 'project', label: '專案', content: project?.project.name ?? item.projectKey };
}
