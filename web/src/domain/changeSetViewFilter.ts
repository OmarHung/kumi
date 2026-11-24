import type { ProjectBlueprint, ViewFilter } from '../api/blueprint';
import type { Change } from '../api/types';
import { nameIn, type ChangeSetBlueprints } from './changeSetNames';
import { quotedList } from './changeSetTransitions';
import { STATE_CATEGORY_LABELS } from './labels';

/**
 * 檢視的 filter 改了：說出改了什麼（類型、狀態、類別、負責人），不只說「篩選條件有調整」，核准的人才知道檢視變寬還是變窄。
 * 清單是空的就是不篩選（和沒寫一樣）。
 */

const ASSIGNEE_LABELS: Readonly<Record<NonNullable<ViewFilter['assignee']>, string>> = {
  me: '看的人自己',
  unassigned: '沒有負責人',
};

function filterOf(blueprint: ProjectBlueprint, change: Change): ViewFilter {
  return blueprint.views?.find((view) => view.key === change.key)?.filter ?? {};
}

/** 狀態篩選只有 key、不分流程：先找改之後的藍圖裡任何一個流程，再找改之前的，都沒有用 key。 */
function stateName({ before, after }: ChangeSetBlueprints, key: string): string {
  const find = (blueprint: ProjectBlueprint) =>
    blueprint.workflows.flatMap((workflow) => workflow.states).find((state) => state.key === key)?.name;
  return find(after) ?? find(before) ?? key;
}

/** 一個清單型的條件：改成了哪些、多了哪些、拿掉了哪些；沒變是空字串。 */
function listText<K extends string>(label: string, old: readonly K[], next: readonly K[], name: (key: K) => string): string {
  const names = (keys: readonly K[]) => quotedList(keys.map(name));
  if (old.join('\n') === next.join('\n')) return '';
  if (old.length === 0) return `${label}篩選改成${names(next)}`;
  if (next.length === 0) return `不再篩選${label}（原本是${names(old)}）`;
  const added = next.filter((key) => !old.includes(key));
  const removed = old.filter((key) => !next.includes(key));
  const parts = [added.length > 0 ? `多了${names(added)}` : '', removed.length > 0 ? `拿掉${names(removed)}` : ''];
  const text = parts.filter((part) => part !== '').join('，');
  return text === '' ? `調整了${label}篩選的順序` : `${label}篩選${text}`;
}

function assigneeText(old: ViewFilter['assignee'], next: ViewFilter['assignee']): string {
  if (old === next) return '';
  return next === undefined ? '不再篩選負責人' : `負責人篩選改成「${ASSIGNEE_LABELS[next]}」`;
}

/** 「類型篩選拿掉「需求」；狀態篩選改成「待辦」」；這四種都沒變（別的屬性改了）時退回「篩選條件有調整」。 */
export function viewFilterText(change: Change, context: ChangeSetBlueprints): string {
  const old = filterOf(context.before, change);
  const next = filterOf(context.after, change);
  const parts = [
    listText('類型', old.itemTypes ?? [], next.itemTypes ?? [], (key) => nameIn(context.after, context.before, 'item_type', key)),
    listText('狀態', old.states ?? [], next.states ?? [], (key) => stateName(context, key)),
    listText('類別', old.stateCategories ?? [], next.stateCategories ?? [], (key) => STATE_CATEGORY_LABELS[key]),
    assigneeText(old.assignee, next.assignee),
  ].filter((part) => part !== '');
  return parts.length === 0 ? '篩選條件有調整' : parts.join('；');
}
