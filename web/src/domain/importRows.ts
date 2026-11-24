import type { FieldDefinition, ProjectBlueprint } from '../api/blueprint';
import type { ImportedItemRequest } from '../api/itemImport';
import type { FieldValue } from '../api/types';
import { findItemType } from './blueprint';
import type { CsvRecord } from './csv';
import { importTargets, type ColumnMapping, type ImportTarget } from './importTargets';
import {
  convertCheckbox,
  convertDate,
  convertItemKey,
  convertMember,
  convertMultiLine,
  convertMultiOptions,
  convertNamed,
  convertNumber,
  convertOption,
  convertPriority,
  convertSingleLine,
  convertState,
  convertType,
  type Converted,
  type ImportMember,
  type NamedRef,
} from './importValues';

/** 把檔案的一列轉成伺服器要的項目（PM-4b）；轉不了的是那一列的問題，列號是檔案的列號。 */
export interface ImportContext {
  readonly blueprint: ProjectBlueprint;
  readonly members: readonly ImportMember[];
  readonly cycles: readonly NamedRef[];
  readonly milestones: readonly NamedRef[];
  /** 沒有任何欄位對應到「類型」時，每一列用這個類型。 */
  readonly defaultType: string;
}

export interface RowIssue {
  /** 檔案的列號。 */
  readonly line: number;
  /** 欄位的名稱（畫面上的說法）；整列的問題是 null。 */
  readonly field: string | null;
  readonly message: string;
}

export interface ConvertedRow {
  readonly line: number;
  readonly request: ImportedItemRequest;
  /** 預覽用：每個對應到的目標轉好的值（空白的格子沒有）。 */
  readonly preview: ReadonlyMap<ImportTarget, string>;
}

export interface ConversionResult {
  readonly rows: readonly ConvertedRow[];
  readonly issues: readonly RowIssue[];
}

interface Patch {
  readonly core?: Partial<ImportedItemRequest>;
  readonly field?: readonly [string, FieldValue];
}

interface Column {
  readonly index: number;
  readonly target: ImportTarget;
  readonly label: string;
}

function toPatch<T>(result: Converted<T>, convert: (value: T) => Patch): Converted<Patch> {
  return result.ok ? { ok: true, value: convert(result.value), display: result.display } : result;
}

/** 核心欄位：這一格轉成伺服器要的值。需要類型的（狀態）在 typeKey 是 undefined 時由呼叫端略過。 */
function convertCore(target: ImportTarget, raw: string, ctx: ImportContext, typeKey: string): Converted<Patch> | null {
  switch (target) {
    case 'title':
      return toPatch(convertSingleLine(raw), (title) => ({ core: { title } }));
    case 'type':
      return toPatch(convertType(raw, ctx.blueprint), (type) => ({ core: { type } }));
    case 'state':
      return toPatch(convertState(raw, ctx.blueprint, typeKey), (state) => ({ core: { state } }));
    case 'assignee':
      return toPatch(convertMember(raw, ctx.members), (assigneeId) => ({ core: { assigneeId } }));
    case 'priority':
      return toPatch(convertPriority(raw), (priority) => ({ core: { priority } }));
    case 'startDate':
      return toPatch(convertDate(raw), (startDate) => ({ core: { startDate } }));
    case 'dueDate':
      return toPatch(convertDate(raw), (dueDate) => ({ core: { dueDate } }));
    case 'estimate':
      return toPatch(convertNumber(raw), (estimate) => ({ core: { estimate } }));
    case 'parent':
      return toPatch(convertItemKey(raw), (parentKey) => ({ core: { parentKey } }));
    case 'description':
      return toPatch(convertMultiLine(raw), (description) => ({ core: { description } }));
    case 'cycle':
      return toPatch(convertNamed(raw, '週期', ctx.cycles), (cycleId) => ({ core: { cycleId } }));
    case 'milestone':
      return toPatch(convertNamed(raw, '里程碑', ctx.milestones), (milestoneId) => ({ core: { milestoneId } }));
    default:
      return null;
  }
}

function convertFieldValue(field: FieldDefinition, raw: string, ctx: ImportContext): Converted<FieldValue> {
  switch (field.type) {
    case 'long_text':
      return convertMultiLine(raw);
    case 'number':
    case 'currency':
      return convertNumber(raw);
    case 'date':
      return convertDate(raw);
    case 'single_select':
      return convertOption(raw, field.options ?? []);
    case 'multi_select':
      return convertMultiOptions(raw, field.options ?? []);
    case 'person':
      return convertMember(raw, ctx.members);
    case 'checkbox':
      return convertCheckbox(raw);
    case 'text':
    case 'url':
      return convertSingleLine(raw);
  }
}

function convertCustom(key: string, raw: string, ctx: ImportContext, typeKey: string): Converted<Patch> {
  const field = ctx.blueprint.fields.find((candidate) => candidate.key === key);
  if (field === undefined) return { ok: false, message: '專案已經沒有這個欄位' };
  const type = findItemType(ctx.blueprint, typeKey);
  if (type !== undefined && !(type.fields ?? []).includes(key)) {
    return { ok: false, message: `「${type.name}」這個類型沒有這個欄位` };
  }
  return toPatch(convertFieldValue(field, raw, ctx), (value) => ({ field: [key, value] }));
}

function convertCell(column: Column, raw: string, ctx: ImportContext, typeKey: string): Converted<Patch> {
  const custom = column.target.startsWith('field:') ? column.target.slice('field:'.length) : null;
  if (custom !== null) return convertCustom(custom, raw, ctx, typeKey);
  return convertCore(column.target, raw, ctx, typeKey) ?? { ok: false, message: '不認得的欄位' };
}

function issueOf(line: number, field: string | null, message: string): RowIssue {
  return { line, field, message };
}

function resolveType(record: CsvRecord, typeColumn: Column | undefined, ctx: ImportContext): Converted<string> {
  if (typeColumn === undefined) return { ok: true, value: ctx.defaultType, display: ctx.defaultType };
  const raw = record.cells[typeColumn.index] ?? '';
  if (raw.trim() === '') return { ok: false, message: '類型是空白的' };
  return convertType(raw, ctx.blueprint);
}

function buildRequest(typeKey: string, patches: readonly Patch[]): ImportedItemRequest {
  const core = Object.assign({}, ...patches.map((patch) => patch.core ?? {})) as Partial<ImportedItemRequest>;
  const fields = patches.flatMap((patch) => (patch.field === undefined ? [] : [patch.field]));
  return {
    ...core,
    type: typeKey,
    title: core.title ?? '',
    ...(fields.length > 0 ? { fields: Object.fromEntries(fields) } : {}),
  };
}

interface ColumnsResult {
  readonly patches: readonly Patch[];
  readonly preview: ReadonlyMap<ImportTarget, string>;
  readonly issues: readonly RowIssue[];
}

/** 一列的每一個對應到的欄位：空白的格子不填；類型已經有問題時，其他欄位（標題除外）不再檢查。 */
function convertColumns(record: CsvRecord, columns: readonly Column[], type: Converted<string>, ctx: ImportContext): ColumnsResult {
  const patches: Patch[] = [];
  const preview = new Map<ImportTarget, string>();
  const issues: RowIssue[] = [];
  for (const column of columns) {
    const raw = record.cells[column.index] ?? '';
    if (column.target === 'type') {
      if (type.ok) preview.set('type', type.display);
    } else if (raw.trim() === '') {
      if (column.target === 'title') issues.push(issueOf(record.line, column.label, '標題是空白的'));
    } else if (type.ok || column.target === 'title') {
      const result = convertCell(column, raw, ctx, type.ok ? type.value : ctx.defaultType);
      if (result.ok) {
        patches.push(result.value);
        preview.set(column.target, result.display);
      } else {
        issues.push(issueOf(record.line, column.label, result.message));
      }
    }
  }
  return { patches, preview, issues };
}

function convertRow(record: CsvRecord, columns: readonly Column[], ctx: ImportContext): { row: ConvertedRow | null; issues: readonly RowIssue[] } {
  const typeColumn = columns.find((column) => column.target === 'type');
  const type = resolveType(record, typeColumn, ctx);
  const typeIssues = type.ok ? [] : [issueOf(record.line, typeColumn?.label ?? '類型', type.message)];
  const { patches, preview, issues } = convertColumns(record, columns, type, ctx);
  const allIssues = [...typeIssues, ...issues];
  if (allIssues.length > 0 || !type.ok) return { row: null, issues: allIssues };
  return { row: { line: record.line, request: buildRequest(type.value, patches), preview }, issues: allIssues };
}

/** 資料列照檔案的順序轉好；有問題的列不在 rows 裡，問題在 issues（照列號、欄位的順序）。 */
export function convertRows(records: readonly CsvRecord[], mapping: ColumnMapping, ctx: ImportContext): ConversionResult {
  const labels = new Map(importTargets(ctx.blueprint).map((option) => [option.id, option.label]));
  const columns = mapping.flatMap((target, index): Column[] =>
    target === null ? [] : [{ index, target, label: labels.get(target) ?? target }],
  );
  const results = records.map((record) => convertRow(record, columns, ctx));
  return {
    rows: results.flatMap((result) => (result.row === null ? [] : [result.row])),
    issues: results.flatMap((result) => result.issues),
  };
}
