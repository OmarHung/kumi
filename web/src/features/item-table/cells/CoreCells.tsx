import { Link } from 'react-router';
import { useParentCandidates } from '../../../api/queries';
import type { Item, Member, PersonRef, Priority } from '../../../api/types';
import { errorMessage } from '../../../api/http';
import { findItemType, itemTypeName } from '../../../domain/blueprint';
import { formatDate } from '../../../domain/dates';
import { formatNumber } from '../../../domain/fields';
import { PRIORITY_LABELS } from '../../../domain/labels';
import { NO_TITLES, usePersonTitles, type PersonTitles } from '../../people/usePersonTitles';
import { DueDateText, EmptyValue, PriorityText } from '../../work-items/ItemCells';
import { useItemPanelLink } from '../../item-page/ItemPanelContext';
import { itemPagePath, useItemPageEntry } from '../../work-items/itemLinks';
import { NobodyMark, personOption } from '../../work-items/selectOptionMarks';
import { useProjectTable } from '../ProjectTableContext';
import { DateEditor } from './DateEditor';
import { DATE_MESSAGE, dateValue, isValidDateDraft, MAX_TITLE_LENGTH, NUMBER_MESSAGE, numberDraft, parseNumber } from './drafts';
import { EditableCell, type EditorApi, type FieldEditSpec } from './EditableCell';
import { InputEditor, SelectEditor, type SelectOption } from './editors';
import { invalidCommand, patchCommand, UNCHANGED, type CellCommand } from './useItemCommand';
import styles from './Cells.module.css';

interface ItemCellProps {
  readonly item: Item;
}

/**
 * 編號是純文字（等寬字）。打開項目的連結只有標題一個（TitleLinkCell）：同一列兩個連到同一處的連結，
 * 鍵盤與報讀軟體都要多停一次。
 */
export function KeyCell({ item }: ItemCellProps) {
  return <span className={styles.key}>{item.key}</span>;
}

/** 建立者：唯讀的人名（建立之後不能改，沒有編輯的格子）。 */
export function CreatedByCell({ item }: ItemCellProps) {
  return <span className={styles.static}>{item.createdBy.displayName}</span>;
}

export function TypeCell({ item }: ItemCellProps) {
  const { detail } = useProjectTable();
  return <span className={styles.static}>{itemTypeName(detail.blueprint, item.type)}</span>;
}

/*
 * 每個核心欄位的編輯方式（*Spec）：格子與補填對話框（fillFields.tsx）共用，編輯器、要送的內容、錯誤對到的 path 只寫一次。
 */

function titleCommand(item: Item, draft: string): CellCommand {
  const title = draft.trim();
  if (title === '') {
    return invalidCommand('請填標題。');
  }
  return title === item.title ? UNCHANGED : patchCommand({ title });
}

/**
 * 表格裡的標題：打開項目的連結（編號是純文字），不就地編輯（理由：標題是打開項目最自然的地方，就地改標題容易誤觸；
 * 要改標題在項目的內容裡，面板與整頁的標題本來就能改）。一行、放不下用省略號，完整的標題放在 title。
 * 寬螢幕一般的點擊在旁邊開項目面板（href 照樣是整頁，⌘／Ctrl 點開新分頁），開著的那一筆標 aria-current。
 */
export function TitleLinkCell({ item }: ItemCellProps) {
  const entry = useItemPageEntry();
  const panelLink = useItemPanelLink(item.key);
  return (
    <Link to={itemPagePath(item.projectKey, item.key)} state={entry} className={styles.titleLink} title={item.title} {...panelLink}>
      <span className={styles.titleLine}>{item.title}</span>
    </Link>
  );
}

/** 項目內容裡的標題（面板、整頁）：點了就地編輯。 */
export function TitleCell({ item }: ItemCellProps) {
  const { can } = useProjectTable();
  return (
    <EditableCell
      item={item}
      label="標題"
      text={item.title}
      canEdit={can.update}
      errorPaths={['title']}
      initialDraft={() => item.title}
      toCommand={(draft) => titleCommand(item, draft)}
      renderEditor={(api) => <InputEditor api={api} maxLength={MAX_TITLE_LENGTH} />}
    >
      <span className={styles.title}>{item.title}</span>
    </EditableCell>
  );
}

/**
 * 成員的選項。current 是目前的值：他已經不是成員（被移出專案）時接在最後，選單才選得到他、不會看起來像沒有人；
 * 伺服器不讓再把他填進去，但選回他就是沒改，不會送出。
 * 每一個選項前面都有小圖（規範 7.3）：成員是頭像，「沒有負責人」是虛線的空圓，字才對齊。
 */
export function memberOptions(
  members: readonly Member[],
  emptyLabel: string,
  current: PersonRef | null = null,
  titles: PersonTitles = NO_TITLES,
): SelectOption[] {
  const isFormer = current !== null && !members.some((member) => member.personId === current.personId);
  return [
    { value: '', label: emptyLabel, leading: <NobodyMark /> },
    ...members.map((member) => personOption(member, false, titles.get(member.personId))),
    ...(isFormer ? [personOption(current, false, titles.get(current.personId))] : []),
  ];
}

/** 負責人的下拉：打開編輯時才讀職稱（人員清單），選項名稱下面寫職稱。 */
function AssigneeEditor({ api, members, current }: { readonly api: EditorApi<string>; readonly members: readonly Member[]; readonly current: PersonRef | null }) {
  const titles = usePersonTitles();
  return <SelectEditor api={api} options={memberOptions(members, '沒有負責人', current, titles)} />;
}

export function assigneeSpec(item: Item, members: readonly Member[]): FieldEditSpec<string> {
  const current = item.assignee?.personId ?? '';
  return {
    label: '負責人',
    errorPaths: ['assignee', 'assigneeId'],
    initialDraft: () => current,
    toCommand: (draft) => (draft === current ? UNCHANGED : patchCommand({ assigneeId: draft || null })),
    renderEditor: (api) => <AssigneeEditor api={api} members={members} current={item.assignee} />,
  };
}

interface AssigneeCellProps extends ItemCellProps {
  /** 負責人改好之後（項目面板與項目頁用它處理「改給別人之後自己看不到了」）。 */
  readonly onAssigned?: (saved: Item) => void;
}

export function AssigneeCell({ item, onAssigned }: AssigneeCellProps) {
  const { detail, can } = useProjectTable();
  const name = item.assignee?.displayName ?? '';
  return (
    <EditableCell item={item} text={name} canEdit={can.update} onSaved={onAssigned} {...assigneeSpec(item, detail.members)}>
      {name === '' ? <EmptyValue label="沒有負責人" /> : name}
    </EditableCell>
  );
}

const PRIORITIES: readonly Priority[] = ['none', 'low', 'medium', 'high', 'urgent'];

function isPriority(value: string): value is Priority {
  return PRIORITIES.some((priority) => priority === value);
}

const PRIORITY_OPTIONS = PRIORITIES.map((priority) => ({ value: priority, label: PRIORITY_LABELS[priority] }));

export function prioritySpec(item: Item): FieldEditSpec<string> {
  return {
    label: '優先度',
    errorPaths: ['priority'],
    initialDraft: () => item.priority,
    toCommand: (draft) => (draft === item.priority || !isPriority(draft) ? UNCHANGED : patchCommand({ priority: draft })),
    renderEditor: (api) => <SelectEditor api={api} options={PRIORITY_OPTIONS} />,
  };
}

export function PriorityCell({ item }: ItemCellProps) {
  const { can } = useProjectTable();
  return (
    <EditableCell
      item={item}
      text={item.priority === 'none' ? '' : PRIORITY_LABELS[item.priority]}
      canEdit={can.update}
      {...prioritySpec(item)}
    >
      <PriorityText priority={item.priority} />
    </EditableCell>
  );
}

export type DateField = 'startDate' | 'dueDate';

interface DateCellProps extends ItemCellProps {
  readonly field: DateField;
}

const DATE_CELLS = {
  startDate: { label: '開始日', paths: ['start_date', 'startDate'] },
  dueDate: { label: '到期日', paths: ['due_date', 'dueDate'] },
} as const;

function dateCommand(field: DateField, current: string | null, draft: string): CellCommand {
  if (!isValidDateDraft(draft)) return invalidCommand(DATE_MESSAGE);
  const value = dateValue(draft);
  return value === current ? UNCHANGED : patchCommand({ [field]: value });
}

export function dateSpec(item: Item, field: DateField): FieldEditSpec<string> {
  const { label, paths } = DATE_CELLS[field];
  const current = item[field];
  return {
    label,
    errorPaths: paths,
    initialDraft: () => current ?? '',
    toCommand: (draft) => dateCommand(field, current, draft),
    renderEditor: (api) => <DateEditor api={api} />,
  };
}

export function DateCell({ item, field }: DateCellProps) {
  const { can, todayDate } = useProjectTable();
  const current = item[field] ?? '';
  const display =
    field === 'dueDate' ? (
      <DueDateText dueDate={item.dueDate} category={item.stateCategory} today={todayDate} />
    ) : (
      current === '' ? <EmptyValue label="沒有開始日" /> : formatDate(current)
    );
  return (
    <EditableCell item={item} text={current === '' ? '' : formatDate(current)} canEdit={can.update} {...dateSpec(item, field)}>
      {display}
    </EditableCell>
  );
}

function estimateCommand(item: Item, draft: string): CellCommand {
  const value = parseNumber(draft);
  if (value === 'invalid') {
    return invalidCommand(NUMBER_MESSAGE);
  }
  return value === item.estimate ? UNCHANGED : patchCommand({ estimate: value });
}

export function estimateSpec(item: Item): FieldEditSpec<string> {
  return {
    label: '估計',
    errorPaths: ['estimate'],
    initialDraft: () => numberDraft(item.estimate),
    toCommand: (draft) => estimateCommand(item, draft),
    renderEditor: (api) => <InputEditor api={api} inputMode="decimal" />,
  };
}

export function EstimateCell({ item }: ItemCellProps) {
  const { can } = useProjectTable();
  const text = item.estimate === null ? '' : formatNumber(item.estimate);
  return (
    <EditableCell item={item} text={text} canEdit={can.update} {...estimateSpec(item)}>
      {text === '' ? <EmptyValue label="沒有估計" /> : text}
    </EditableCell>
  );
}

function ParentEditor({ api, item }: { readonly api: EditorApi<string>; readonly item: Item }) {
  const { detail } = useProjectTable();
  const types = findItemType(detail.blueprint, item.type)?.parents ?? [];
  const candidates = useParentCandidates(item.projectKey, types, true);
  const loaded = (candidates.data ?? []).filter((candidate) => candidate.key !== item.key);
  const options: SelectOption[] = [
    { value: '', label: '沒有上層項目' },
    ...loaded.map((candidate) => ({ value: candidate.key, label: `${candidate.key} ${candidate.title}` })),
  ];
  const isMissing = api.draft !== '' && !options.some((option) => option.value === api.draft);
  return (
    <>
      <SelectEditor api={api} options={isMissing ? [...options, { value: api.draft, label: api.draft }] : options} />
      {candidates.isPending && <span className={styles.hint}>正在載入可以選的項目…</span>}
      {candidates.isError && <span className={styles.hint}>{errorMessage(candidates.error)}</span>}
    </>
  );
}

export function parentSpec(item: Item): FieldEditSpec<string> {
  const current = item.parentKey ?? '';
  return {
    label: '上層項目',
    errorPaths: ['parent', 'parentKey'],
    initialDraft: () => current,
    toCommand: (draft) => (draft === current ? UNCHANGED : patchCommand({ parentKey: draft || null })),
    renderEditor: (api) => <ParentEditor api={api} item={item} />,
  };
}

export function ParentCell({ item }: ItemCellProps) {
  const { can } = useProjectTable();
  const current = item.parentKey ?? '';
  return (
    <EditableCell item={item} text={current} canEdit={can.update} {...parentSpec(item)}>
      {current === '' ? <EmptyValue label="沒有上層項目" /> : <span className={styles.mono}>{current}</span>}
    </EditableCell>
  );
}
