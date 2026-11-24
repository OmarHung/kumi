import type { FieldDraft, SettingsDraft, WorkflowDraft } from './settingsDraft';
import type { ProjectBlueprint } from '../api/blueprint';
import { itemTypeIssues, reachabilityIssues, requiredFieldIssues, roleIssues, transitionRoleIssues } from './settingsChecks';
import type { SettingsIssue } from './settingsIssues';
import { BLUEPRINT_KEY_PATTERN, hasControlCharacter, keyFormatError, nameError } from './settingsNames';
import { removedStates } from './settingsStatus';

/**
 * 設定頁在送出前能先擋的檢查，和伺服器的語意規則相同。前端先擋只是為了早一點說明，最後以伺服器的 changeset.invalid 為準。
 */

export { BLUEPRINT_KEY_PATTERN, KEY_HINT, NAME_MAX_LENGTH, nameError } from './settingsNames';

const KEY_MAX_LENGTH = 40;
/** 建議的 key 後面還要接編號，前面那段先留一點空間。 */
const SUGGESTED_STEM_LENGTH = 36;
/** 英文名稱轉出來的 key 被用掉時，往後試到 _99；再不行就改用前綴加編號。 */
const MAX_SUGGESTED_SUFFIX = 99;

/** 給核准的人看的一句話；和伺服器的上限一樣（WorkRules.MaxChangeSetSummaryLength），前端先擋。 */
export const SUMMARY_MAX_LENGTH = 500;

/** 內建在每個項目上的欄位，自訂欄位不能同名（活動紀錄用同一個名字指稱兩者）。 */
export const CORE_FIELD_KEYS: ReadonlySet<string> = new Set([
  'title',
  'description',
  'state',
  'assignee',
  'priority',
  'start_date',
  'due_date',
  'estimate',
  'parent',
  'item_type',
]);


function stateKeysOf(base: ProjectBlueprint, draft: SettingsDraft, workflowKey: string): ReadonlySet<string> {
  const before = base.workflows.find((workflow) => workflow.key === workflowKey)?.states ?? [];
  const after = draft.workflows.find((workflow) => workflow.key === workflowKey)?.states ?? [];
  return new Set([...before, ...after].map((state) => state.key));
}

/** 新狀態的 key。要刪除的狀態的 key 也算用過：刪掉又加回同一個 key 會讓搬移的指示對不上。 */
export function stateKeyError(base: ProjectBlueprint, draft: SettingsDraft, workflowKey: string, key: string): string | undefined {
  const format = keyFormatError(key);
  if (format !== undefined) return format;
  return stateKeysOf(base, draft, workflowKey).has(key) ? `這個流程已經有 key 是 ${key} 的狀態。` : undefined;
}

export function fieldKeyError(base: ProjectBlueprint, draft: SettingsDraft, key: string): string | undefined {
  const format = keyFormatError(key);
  if (format !== undefined) return format;
  if (CORE_FIELD_KEYS.has(key)) return `「${key}」是內建欄位的名字，自訂欄位不能用。`;
  const taken = [...base.fields, ...draft.fields].some((field) => field.key === key);
  return taken ? `已經有 key 是 ${key} 的欄位。` : undefined;
}

export function optionKeyError(draft: SettingsDraft, fieldKey: string, key: string): string | undefined {
  const format = keyFormatError(key);
  if (format !== undefined) return format;
  const options = draft.fields.find((field) => field.key === fieldKey)?.options ?? [];
  return options.some((option) => option.key === key) ? `這個欄位已經有 key 是 ${key} 的選項。` : undefined;
}

/** 摘要可以不填；填了就和名稱一樣不能有控制字元。 */
export function summaryError(summary: string): string | undefined {
  const trimmed = summary.trim();
  if (trimmed.length > SUMMARY_MAX_LENGTH) return `摘要最多 ${String(SUMMARY_MAX_LENGTH)} 個字。`;
  return hasControlCharacter(trimmed) ? '摘要不能有控制字元。' : undefined;
}

/**
 * 建議的 key：英文的名稱轉成小寫加底線（「QA Review」→ qa_review），其他的（例如中文）用前綴加編號。
 * 已經用過就在後面加 _2、_3…。使用者可以自己改。
 */
export function suggestKey(name: string, prefix: string, taken: ReadonlySet<string>): string {
  const slug = name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .slice(0, SUGGESTED_STEM_LENGTH);
  if (BLUEPRINT_KEY_PATTERN.test(slug)) {
    const suffixes = Array.from({ length: MAX_SUGGESTED_SUFFIX - 1 }, (_, index) => `${slug}_${String(index + 2)}`);
    const candidates = [slug, ...suffixes];
    const free = candidates.find((candidate) => !taken.has(candidate));
    if (free !== undefined && free.length <= KEY_MAX_LENGTH) return free;
  }
  let number = 1;
  while (taken.has(`${prefix}_${String(number)}`)) number += 1;
  return `${prefix}_${String(number)}`;
}

function workflowIssues(base: ProjectBlueprint, draft: SettingsDraft, workflow: WorkflowDraft): readonly SettingsIssue[] {
  const name = base.workflows.find((candidate) => candidate.key === workflow.key)?.name ?? workflow.key;
  const target = { kind: 'workflow', workflow: workflow.key } as const;
  const structural: SettingsIssue[] = [];
  if (workflow.states.length === 0) {
    structural.push({ target, message: `流程「${name}」至少要留一個狀態。` });
  } else if (!workflow.states.some((state) => state.category === 'done')) {
    structural.push({ target, message: `流程「${name}」至少要有一個類別是「完成」的狀態。` });
  }
  const names = workflow.states.flatMap((state) => {
    const message = nameError(state.name);
    return message === undefined ? [] : [{ target: { kind: 'state', workflow: workflow.key, state: state.key } as const, message }];
  });
  const moves = removedStates(base, draft, workflow.key).flatMap(({ state, to }) =>
    to === ''
      ? [{ target: { kind: 'state', workflow: workflow.key, state: state.key } as const, message: `請選擇停在「${state.name}」的項目要搬到哪個狀態。` }]
      : [],
  );
  return [
    ...structural,
    ...names,
    ...moves,
    ...reachabilityIssues(workflow),
    ...requiredFieldIssues(base, draft, workflow),
    ...transitionRoleIssues(draft, workflow),
  ];
}

function fieldIssues(field: FieldDraft): readonly SettingsIssue[] {
  const target = { kind: 'field', field: field.key } as const;
  const name = nameError(field.name);
  const options = (field.options ?? []).flatMap((option) => {
    const message = nameError(option.name);
    return message === undefined ? [] : [{ target: { kind: 'option', field: field.key, option: option.key } as const, message }];
  });
  const isSelect = field.type === 'single_select' || field.type === 'multi_select';
  const empty = isSelect && (field.options ?? []).length === 0 ? [{ target, message: '選項型的欄位至少要有一個選項。' }] : [];
  return [...(name === undefined ? [] : [{ target, message: name }]), ...options, ...empty];
}

/**
 * 送出前的檢查。照畫面上的順序：每個流程（流程本身、狀態、要刪除的狀態、走不走得到、轉換要先填的欄位與允許的角色），
 * 再來是類型、欄位（名稱、選項）與角色。
 */
export function validateDraft(base: ProjectBlueprint, draft: SettingsDraft): readonly SettingsIssue[] {
  return [
    ...draft.workflows.flatMap((workflow) => workflowIssues(base, draft, workflow)),
    ...itemTypeIssues(base, draft),
    ...draft.fields.flatMap(fieldIssues),
    ...roleIssues(draft),
  ];
}
