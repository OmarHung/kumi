import { columnVisibilityFeature, createColumnHelper, tableFeatures } from '@tanstack/react-table';
import type { ReactNode } from 'react';
import type { FieldType, ProjectBlueprint } from '../../api/blueprint';
import type { Item, ItemSortField } from '../../api/types';
import { fieldSort, isSortableField } from '../../domain/fieldFilters';
import { anyTypeHasParents, typeAllowsParent, typeHasField } from '../../domain/fields';
import {
  AssigneeCell,
  CreatedByCell,
  DateCell,
  EstimateCell,
  KeyCell,
  ParentCell,
  PriorityCell,
  TitleLinkCell,
  TypeCell,
} from './cells/CoreCells';
import { SelectCell } from '../bulk-edit/SelectCells';
import { FieldCell } from './cells/FieldCells';
import { CycleCell, MilestoneCell } from './cells/PlanningCells';
import { StateCell } from './cells/StateCell';

/**
 * 欄的預設寬度是哪一種（使用者沒有拉過時，ItemTable.module.css 的區域變數，docs/DESIGN.md 7.1「欄寬」）：
 * short 優先度、估計、數字、勾選；name 狀態、負責人、類型、單選、成員、金額、上層項目；date 日期；
 * due 到期日（日期加「已過期」）；text 文字、長文字、網址、多選。編號與標題是固定欄，寬度看 pin。
 */
export type ColumnWidthKind = 'short' | 'name' | 'date' | 'due' | 'text';

/** 欄位的額外資訊：標題文字、能不能排序、固定在左邊、預設寬度、這個項目適不適用。 */
export interface ItemColumnMeta {
  readonly label: string;
  /** 後端支援的排序欄位；沒有的欄不能點標題排序。 */
  readonly sortField?: ItemSortField;
  /** select 是多選的勾選框欄（固定在最左邊，在編號前面）。 */
  readonly pin?: 'select' | 'key' | 'title';
  /** 沒有給的是 name。 */
  readonly width?: ColumnWidthKind;
  readonly isNumeric?: boolean;
  /** 回 false 時格子顯示「不適用」，不能編輯（例如類型沒有這個自訂欄位）。 */
  readonly appliesTo?: (item: Item) => boolean;
}

export const itemTableFeatures = tableFeatures({
  columnVisibilityFeature,
  columnMeta: {} as ItemColumnMeta,
});

export type ItemTableFeatures = typeof itemTableFeatures;

const helper = createColumnHelper<ItemTableFeatures, Item>();

type ItemColumn = ReturnType<typeof helper.display>;

function column(id: string, meta: ItemColumnMeta, render: (item: Item) => ReactNode): ItemColumn {
  return helper.display({
    id,
    // 編號與標題是認項目用的，一定顯示。
    enableHiding: meta.pin === undefined,
    meta,
    cell: ({ row }) => render(row.original),
  });
}

function coreColumns(blueprint: ProjectBlueprint): ItemColumn[] {
  const hasTypes = blueprint.itemTypes.length > 1;
  const typeColumn = column('type', { label: '類型' }, (item) => <TypeCell item={item} />);
  return [
    column('key', { label: '編號', sortField: 'number', pin: 'key' }, (item) => <KeyCell item={item} />),
    column('title', { label: '標題', sortField: 'title', pin: 'title' }, (item) => <TitleLinkCell item={item} />),
    ...(hasTypes ? [typeColumn] : []),
    column('state', { label: '狀態', sortField: 'state' }, (item) => <StateCell item={item} />),
    column('assignee', { label: '負責人' }, (item) => <AssigneeCell item={item} />),
    column('createdBy', { label: '建立者' }, (item) => <CreatedByCell item={item} />),
    column('priority', { label: '優先度', sortField: 'priority', width: 'short' }, (item) => <PriorityCell item={item} />),
    column('start_date', { label: '開始日', sortField: 'start_date', width: 'date' }, (item) => (
      <DateCell item={item} field="startDate" />
    )),
    column('due_date', { label: '到期日', sortField: 'due_date', width: 'due' }, (item) => (
      <DateCell item={item} field="dueDate" />
    )),
    column('estimate', { label: '估計', sortField: 'estimate', isNumeric: true, width: 'short' }, (item) => (
      <EstimateCell item={item} />
    )),
  ];
}

function parentColumn(blueprint: ProjectBlueprint): ItemColumn[] {
  if (!anyTypeHasParents(blueprint)) {
    return [];
  }
  const meta = { label: '上層項目', appliesTo: (item: Item) => typeAllowsParent(blueprint, item.type) };
  return [column('parent', meta, (item) => <ParentCell item={item} />)];
}

/** 專案有沒有週期、里程碑：沒有的那一欄不放進表格，也不會出現在「顯示的欄位」。 */
export interface PlanningColumns {
  readonly hasCycles: boolean;
  readonly hasMilestones: boolean;
}

const NO_PLANNING_COLUMNS: PlanningColumns = { hasCycles: false, hasMilestones: false };

/** 預設不顯示（columnVisibility.ts 的 DEFAULT_HIDDEN；建立者也是），從「顯示的欄位」打開。 */
function planningColumns({ hasCycles, hasMilestones }: PlanningColumns): ItemColumn[] {
  return [
    ...(hasCycles ? [column('cycle', { label: '週期' }, (item) => <CycleCell item={item} />)] : []),
    ...(hasMilestones ? [column('milestone', { label: '里程碑' }, (item) => <MilestoneCell item={item} />)] : []),
  ];
}

const FIELD_WIDTHS: Readonly<Record<FieldType, ColumnWidthKind>> = {
  text: 'text',
  long_text: 'text',
  url: 'text',
  multi_select: 'text',
  number: 'short',
  checkbox: 'short',
  currency: 'name',
  single_select: 'name',
  person: 'name',
  date: 'date',
};

function fieldColumns(blueprint: ProjectBlueprint): ItemColumn[] {
  return blueprint.fields.map((definition) =>
    column(
      `field:${definition.key}`,
      {
        label: definition.name,
        // 能排的型別點標題就排；人員、多選、長文字、網址不能排，標題和不能排的核心欄一樣不是按鈕。
        sortField: isSortableField(definition) ? fieldSort(definition.key) : undefined,
        width: FIELD_WIDTHS[definition.type],
        isNumeric: definition.type === 'number' || definition.type === 'currency',
        appliesTo: (item) => typeHasField(blueprint, item.type, definition.key),
      },
      (item) => <FieldCell item={item} definition={definition} />,
    ),
  );
}

/** 多選的勾選框欄：只有能改、能改狀態或能刪除的人才有（沒有動作可做的人勾了也沒用）。 */
function selectColumn(): ItemColumn[] {
  return [column('select', { label: '選取', pin: 'select' }, (item) => <SelectCell item={item} />)];
}

/**
 * 表格的欄：多選的勾選框（canSelect）、核心欄位、週期與里程碑（專案有才有），再加上藍圖的每個自訂欄位。
 * 類型只有一種時不顯示類型欄。
 */
export function buildColumns(
  blueprint: ProjectBlueprint,
  planning: PlanningColumns = NO_PLANNING_COLUMNS,
  canSelect = false,
): ItemColumn[] {
  return [
    ...(canSelect ? selectColumn() : []),
    ...coreColumns(blueprint),
    ...parentColumn(blueprint),
    ...planningColumns(planning),
    ...fieldColumns(blueprint),
  ];
}

/** 欄位選擇器要列的：可以隱藏的欄。 */
export function hideableColumns(columns: readonly ItemColumn[]): { readonly id: string; readonly label: string }[] {
  return columns.flatMap((candidate) =>
    candidate.meta?.pin === undefined && candidate.id !== undefined
      ? [{ id: candidate.id, label: candidate.meta?.label ?? candidate.id }]
      : [],
  );
}

export type { ItemColumn };
