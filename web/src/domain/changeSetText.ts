import type { ProjectBlueprint } from '../api/blueprint';
import type { Change, ChangeResource } from '../api/types';
import { nameIn, workflowSuffix, type ChangeSetBlueprints } from './changeSetNames';
import { addedRoleSuffix, LIST_PROPERTY_TEXTS } from './changeSetProperties';
import { addedTransitionSuffix, restrictionDetails } from './changeSetTransitions';
import { FIELD_TYPE_LABELS, STATE_CATEGORY_LABELS } from './labels';

export type { ChangeSetBlueprints } from './changeSetNames';

const RESOURCE_LABELS: Readonly<Record<ChangeResource, string>> = {
  blueprint: '藍圖',
  metadata: '專案資訊',
  field: '欄位',
  field_option: '選項',
  workflow: '流程',
  state: '狀態',
  transition: '轉換',
  item_type: '類型',
  view: '檢視',
  automation: '自動化',
  role: '角色',
  binding: '整合',
};

/**
 * 文件裡的屬性名稱轉成看得懂的說法（伺服器比對的屬性見 PmTool.Blueprint 的 BlueprintDiffer）。
 * 清單型的（states、options…）在 ORDER_LABELS；有自己句子的（名稱、狀態的類別、欄位的型別與幣別、類型的欄位）在 propertyText。
 */
const PROPERTY_LABELS: Readonly<Record<string, string>> = {
  key: 'key',
  description: '說明',
  currency: '幣別',
  basedOn: '來源模板',
  color: '顏色',
  workflow: '流程',
  parents: '可以掛的上層類型',
  icon: '圖示',
  type: '類型',
  groupBy: '分組方式',
  columns: '顯示的欄位',
  sort: '排序',
  filter: '篩選條件',
  visibleTo: '看得到的角色',
  enabled: '啟用與否',
  trigger: '觸發條件',
  conditions: '條件',
  actions: '動作',
  permissions: '權限',
  allowedRoles: '允許的角色',
  requireFields: '必填的欄位',
};

/** 同一個屬性名稱在某些資源有不同的意思：檢視的 type 是表格或看板，不是項目的類型。 */
const RESOURCE_PROPERTY_LABELS: Partial<Record<ChangeResource, Readonly<Record<string, string>>>> = {
  view: { type: '顯示方式' },
};

/** 清單型的屬性：出現在差異裡表示兩版都有的項目換了先後，或是這個清單從沒寫變成有寫（反過來也是）。 */
const ORDER_LABELS: Readonly<Record<string, string>> = {
  fields: '欄位',
  workflows: '流程',
  itemTypes: '類型',
  views: '檢視',
  automations: '自動化',
  roles: '角色',
  bindings: '整合',
  states: '狀態',
  transitions: '轉換',
  options: '選項',
};

function quoted(name: string): string {
  return `「${name}」`;
}

function transitionName(blueprint: ProjectBlueprint, other: ProjectBlueprint, change: Change): string {
  const [from = '', to = ''] = change.key.split('->');
  const state = (key: string) => quoted(nameIn(blueprint, other, 'state', key, change.parentKey));
  return `${state(from)}→${state(to)}`;
}

/** 這個資源叫什麼：「欄位「分類」的選項「新功能」」「狀態「待辦」（交付流程）」。first 是先找名稱的那一份藍圖。 */
function subject(change: Change, context: ChangeSetBlueprints, first: ProjectBlueprint): string {
  const second = first === context.after ? context.before : context.after;
  const label = RESOURCE_LABELS[change.resource];
  switch (change.resource) {
    case 'transition':
      return `${label}${transitionName(first, second, change)}${workflowSuffix(context, change.parentKey)}`;
    case 'state':
      return `${label}${quoted(nameIn(first, second, 'state', change.key, change.parentKey))}${workflowSuffix(context, change.parentKey)}`;
    case 'field_option':
      return `欄位${quoted(nameIn(first, second, 'field', change.parentKey ?? ''))}的${label}${quoted(nameIn(first, second, 'field_option', change.key, change.parentKey))}`;
    case 'binding':
      return `${label}${quoted(change.key)}`;
    default:
      return `${label}${quoted(nameIn(first, second, change.resource, change.key, change.parentKey))}`;
  }
}

function fieldTypeSuffix(change: Change, after: ProjectBlueprint): string {
  const type = after.fields.find((field) => field.key === change.key)?.type;
  return type === undefined ? '' : `（${FIELD_TYPE_LABELS[type]}）`;
}

/** 新增的東西核准的人要看得到內容：欄位的型別、轉換的限制、角色的權限。 */
function addedDetail(change: Change, context: ChangeSetBlueprints): string {
  switch (change.resource) {
    case 'field':
      return fieldTypeSuffix(change, context.after);
    case 'transition':
      return addedTransitionSuffix(change, context);
    case 'role':
      return addedRoleSuffix(change, context);
    default:
      return '';
  }
}

function addedText(change: Change, context: ChangeSetBlueprints): string {
  if (change.resource === 'field_option') {
    const field = quoted(nameIn(context.before, context.after, 'field', change.parentKey ?? ''));
    return `在欄位${field}新增選項${quoted(nameIn(context.after, context.before, 'field_option', change.key, change.parentKey))}`;
  }
  return `新增${subject(change, context, context.after)}${addedDetail(change, context)}`;
}

function categoryText(change: Change, { before, after }: ChangeSetBlueprints): string {
  const find = (blueprint: ProjectBlueprint) =>
    blueprint.workflows.find((workflow) => workflow.key === change.parentKey)?.states.find((state) => state.key === change.key);
  const from = find(before)?.category;
  const to = find(after)?.category;
  return `類別從${quoted(from ? STATE_CATEGORY_LABELS[from] : '')}改成${quoted(to ? STATE_CATEGORY_LABELS[to] : '')}`;
}

function fieldTypeText(change: Change, { before, after }: ChangeSetBlueprints): string {
  const from = before.fields.find((field) => field.key === change.key)?.type;
  const to = after.fields.find((field) => field.key === change.key)?.type;
  return `型別從${quoted(from ? FIELD_TYPE_LABELS[from] : '')}改成${quoted(to ? FIELD_TYPE_LABELS[to] : '')}`;
}

function currencyText(change: Change, { before, after }: ChangeSetBlueprints): string {
  const currencyOf = (blueprint: ProjectBlueprint) => blueprint.fields.find((field) => field.key === change.key)?.currency;
  const from = currencyOf(before);
  const to = currencyOf(after);
  if (from === undefined) return `幣別設為${quoted(to ?? '')}`;
  return to === undefined ? '拿掉了幣別' : `幣別從${quoted(from)}改成${quoted(to)}`;
}

/** 可以不寫的清單（欄位的 options；流程的 transitions 有自己的句子）；其他的清單一定有，當成兩版都寫了。 */
function listOf(blueprint: ProjectBlueprint, change: Change, property: string): readonly unknown[] | undefined {
  if (change.resource === 'field' && property === 'options') {
    return blueprint.fields.find((field) => field.key === change.key)?.options;
  }
  return [];
}

/** 兩版都有這個清單是順序變了；只有一版有是加上或拿掉（例如欄位改成選項型時多了選項）。 */
function listText(change: Change, property: string, label: string, { before, after }: ChangeSetBlueprints): string {
  const had = listOf(before, change, property) !== undefined;
  const has = listOf(after, change, property) !== undefined;
  if (had === has) return `調整了${label}的順序`;
  return has ? `加上了${label}` : `拿掉了${label}`;
}

/** 類型的 fields 改了：說出多了哪些、少了哪些欄位；只是換了順序就說順序。 */
function typeFieldsText(change: Change, { before, after }: ChangeSetBlueprints): string {
  const fieldsOf = (blueprint: ProjectBlueprint) => blueprint.itemTypes.find((type) => type.key === change.key)?.fields ?? [];
  const old = fieldsOf(before);
  const next = fieldsOf(after);
  const names = (keys: readonly string[]) => keys.map((key) => quoted(nameIn(after, before, 'field', key))).join('、');
  const added = next.filter((key) => !old.includes(key));
  const removed = old.filter((key) => !next.includes(key));
  const parts = [added.length > 0 ? `多了欄位${names(added)}` : '', removed.length > 0 ? `少了欄位${names(removed)}` : ''];
  const text = parts.filter((part) => part !== '').join('，');
  return text === '' ? '調整了欄位的順序' : text;
}

/** 有自己句子的屬性，以「資源.屬性」為鍵。 */
const PROPERTY_TEXTS: Readonly<Record<string, (change: Change, context: ChangeSetBlueprints) => string>> = {
  'state.category': categoryText,
  'field.type': fieldTypeText,
  'field.currency': currencyText,
  'item_type.fields': typeFieldsText,
  ...LIST_PROPERTY_TEXTS,
};

/** 這些資源的說明常是一串名稱（用「、」分開），好幾個屬性之間改用「；」才分得清楚。 */
const LIST_DETAIL_RESOURCES: ReadonlySet<ChangeResource> = new Set(['transition', 'item_type', 'role', 'view']);

function propertyText(change: Change, property: string, context: ChangeSetBlueprints): string {
  if (property === 'name') {
    return `改名為${quoted(nameIn(context.after, context.before, change.resource, change.key, change.parentKey))}`;
  }
  const special = PROPERTY_TEXTS[`${change.resource}.${property}`];
  if (special !== undefined) return special(change, context);
  const order = ORDER_LABELS[property];
  if (order !== undefined) return listText(change, property, order, context);
  return `${RESOURCE_PROPERTY_LABELS[change.resource]?.[property] ?? PROPERTY_LABELS[property] ?? property}有調整`;
}

function modifiedText(change: Change, context: ChangeSetBlueprints): string {
  if (change.resource === 'blueprint') {
    const lists = change.changedProperties.map((property) => ORDER_LABELS[property] ?? property);
    return `調整了${lists.join('、')}的順序`;
  }
  const target = subject(change, context, context.before);
  const [only] = change.changedProperties;
  if (change.changedProperties.length === 1 && only === 'name') {
    return `把${target}${propertyText(change, only, context)}`;
  }
  const details = change.changedProperties.map((property) => propertyText(change, property, context));
  return `修改${target}：${details.join(LIST_DETAIL_RESOURCES.has(change.resource) ? '；' : '、')}`;
}

/** 一個差異說成一句話，例如「新增狀態「內部驗收」」「刪除欄位「備註」」「把狀態「審查中」改名為「內部審查」」。 */
export function describeBlueprintChange(change: Change, context: ChangeSetBlueprints): string {
  switch (change.kind) {
    case 'added':
      return addedText(change, context);
    case 'removed':
      return `刪除${subject(change, context, context.before)}`;
    case 'modified':
      return modifiedText(change, context);
  }
}

/** 句子底下一行一條的內容（從不限制切成限制、轉換太多寫不進一句時）；大多數的修改沒有。 */
export function blueprintChangeDetails(change: Change, context: ChangeSetBlueprints): readonly string[] {
  return restrictionDetails(change, context);
}
