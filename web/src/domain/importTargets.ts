import type { ProjectBlueprint } from '../api/blueprint';

/**
 * 匯入時「這一欄要匯入成什麼」（PM-4b）。核心欄位用固定的代號；自訂欄位是 `field:<key>`。
 */
export type CoreImportTarget =
  | 'title'
  | 'type'
  | 'state'
  | 'assignee'
  | 'priority'
  | 'startDate'
  | 'dueDate'
  | 'estimate'
  | 'parent'
  | 'description'
  | 'cycle'
  | 'milestone';

export type ImportTarget = CoreImportTarget | `field:${string}`;

/** 檔案每一欄對到的目標；null 是不匯入。長度和標題列的格數一樣。 */
export type ColumnMapping = readonly (ImportTarget | null)[];

export interface ImportTargetOption {
  readonly id: ImportTarget;
  readonly label: string;
  /** 標題列的字和這些（或 label、自訂欄位的 key）一樣就自動對應；比對前先 normalizeHeader。 */
  readonly aliases: readonly string[];
}

const CORE_TARGETS: readonly ImportTargetOption[] = [
  { id: 'title', label: '標題', aliases: ['title', 'name', '名稱'] },
  { id: 'type', label: '類型', aliases: ['type', 'itemtype', '種類'] },
  { id: 'state', label: '狀態', aliases: ['status', 'state'] },
  { id: 'assignee', label: '負責人', aliases: ['assignee', 'owner', 'assignedto', '指派', '指派給'] },
  { id: 'priority', label: '優先度', aliases: ['priority', '優先級', '優先順序'] },
  { id: 'startDate', label: '開始日', aliases: ['start', 'startdate', '開始', '開始日期'] },
  { id: 'dueDate', label: '到期日', aliases: ['due', 'duedate', 'deadline', '到期', '截止日', '截止日期', '到期日期'] },
  { id: 'estimate', label: '估計', aliases: ['estimate', 'estimation', '估算', '預估'] },
  { id: 'parent', label: '上層項目（編號）', aliases: ['parent', 'parentkey', 'parentid', '上層', '上層項目'] },
  { id: 'description', label: '說明', aliases: ['description', 'desc', '描述', '內容'] },
  { id: 'cycle', label: '週期', aliases: ['cycle', 'sprint', '衝刺'] },
  { id: 'milestone', label: '里程碑', aliases: ['milestone'] },
];

/** 比對標題用：不分大小寫、全形半形、空白、底線與連字號。 */
export function normalizeHeader(text: string): string {
  return text.normalize('NFKC').toLowerCase().replace(/[\s_-]+/g, '');
}

export function customTargetId(fieldKey: string): ImportTarget {
  return `field:${fieldKey}`;
}

/** 這個專案能匯入的所有目標：核心欄位在前，接著藍圖的自訂欄位（藍圖的順序）。 */
export function importTargets(blueprint: ProjectBlueprint): readonly ImportTargetOption[] {
  const custom = blueprint.fields.map(
    (field): ImportTargetOption => ({ id: customTargetId(field.key), label: field.name, aliases: [field.key] }),
  );
  return [...CORE_TARGETS, ...custom];
}

function matches(option: ImportTargetOption, header: string): boolean {
  return [option.label, ...option.aliases].some((name) => normalizeHeader(name) === header);
}

/**
 * 依標題列自動對應。一個目標只給一欄（先到先得）；excluded 裡的目標不自動對應
 * （沒有 project.manage 的人不能匯入狀態）。
 */
export function autoMapColumns(
  headers: readonly string[],
  targets: readonly ImportTargetOption[],
  excluded: ReadonlySet<ImportTarget> = new Set(),
): ColumnMapping {
  const taken = new Set<ImportTarget>();
  return headers.map((header) => {
    const normalized = normalizeHeader(header);
    if (normalized === '') return null;
    const found = targets.find((option) => !taken.has(option.id) && !excluded.has(option.id) && matches(option, normalized));
    if (found === undefined) return null;
    taken.add(found.id);
    return found.id;
  });
}

/** 把一欄改對到另一個目標；同一個目標原本在別欄的話，那一欄換成不匯入。 */
export function assignTarget(mapping: ColumnMapping, columnIndex: number, target: ImportTarget | null): ColumnMapping {
  return mapping.map((current, index) => {
    if (index === columnIndex) return target;
    return target !== null && current === target ? null : current;
  });
}

export function isMapped(mapping: ColumnMapping, target: ImportTarget): boolean {
  return mapping.includes(target);
}
