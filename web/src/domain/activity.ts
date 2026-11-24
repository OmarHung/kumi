import type { FieldDefinition, ProjectBlueprint } from '../api/blueprint';
import type { Cycle, Milestone } from '../api/planning';
import type { Activity, FieldChange, FieldValue, Member } from '../api/types';
import { findState, itemTypeName } from './blueprint';
import { formatDate } from './dates';
import { fieldDefinition, fieldLabel, formatFieldValue, formatNumber } from './fields';
import { PRIORITY_LABELS } from './labels';
import { displayCycleName, displayMilestoneName } from './planningFilter';

/** 刪除的紀錄：項目刪除之後讀不到，時間軸其實不會出現這一筆。 */
export const DELETED_TEXT = '刪除了這個項目';

/** 活動裡的人（負責人、成員欄位）不在成員清單裡時的說法。 */
export const UNKNOWN_PERSON = '查不到的人';

export interface ActivityContext {
  readonly blueprint: ProjectBlueprint;
  readonly members: readonly Member[];
  /**
   * 專案的週期與里程碑：活動只記編號，名稱用目前的。清單還沒載入時是 undefined，說法用刪節號，不把還沒載入的當成已刪除；
   * 讀不到時是 null，說法用中性的「某個週期」，也不說成已刪除。
   */
  readonly cycles?: readonly Cycle[] | null | undefined;
  readonly milestones?: readonly Milestone[] | null | undefined;
  /** 這一筆活動當時項目的類型（typesAtEntries）：狀態的名稱要看這個類型的流程。 */
  readonly typeKey: string;
}

/** 一句話裡的一段：值另外標出來，畫面上比較好掃讀。 */
export interface Segment {
  readonly kind: 'text' | 'value';
  readonly text: string;
}

export interface ChangeDescription {
  readonly label: string;
  readonly segments: readonly Segment[];
}

function text(value: string): Segment {
  return { kind: 'text', text: value };
}

function value(content: string): Segment {
  return { kind: 'value', text: content };
}

function personName(personId: FieldValue, members: readonly Member[]): string {
  return members.find((member) => member.personId === personId)?.displayName ?? UNKNOWN_PERSON;
}

function rawText(raw: FieldValue): string {
  return Array.isArray(raw) ? raw.join('、') : String(raw);
}

function isPriorityKey(raw: string): raw is keyof typeof PRIORITY_LABELS {
  return Object.hasOwn(PRIORITY_LABELS, raw);
}

/** 週期與里程碑的值是編號，換成目前的名稱。 */
const NOT_LOADED_TEXT = '…';

function coreValueText(field: string, raw: FieldValue, context: ActivityContext): string | null | undefined {
  const { members, cycles, milestones } = context;
  switch (field) {
    case 'cycle':
      return cycles === undefined ? NOT_LOADED_TEXT : displayCycleName(String(raw), cycles);
    case 'milestone':
      return milestones === undefined ? NOT_LOADED_TEXT : displayMilestoneName(String(raw), milestones);
    case 'assignee':
      return personName(raw, members);
    case 'priority': {
      const key = String(raw);
      // 優先度沒有 null，「未設定」就是沒有值。
      if (key === 'none') return null;
      return isPriorityKey(key) ? PRIORITY_LABELS[key] : key;
    }
    case 'start_date':
    case 'due_date':
      return formatDate(String(raw));
    case 'estimate':
      return typeof raw === 'number' ? formatNumber(raw) : String(raw);
    case 'title':
    case 'parent':
      return String(raw);
    default:
      return undefined;
  }
}

function customValueText(definition: FieldDefinition, raw: FieldValue, members: readonly Member[]): string {
  return definition.type === 'person' ? personName(raw, members) : formatFieldValue(definition, raw, members);
}

/** 值轉成看得懂的文字，和表格的顯示方式一樣。沒有值是 null。 */
function valueText(field: string, raw: FieldValue | null, context: ActivityContext): string | null {
  if (raw === null) {
    return null;
  }
  const core = coreValueText(field, raw, context);
  if (core !== undefined) {
    return core;
  }
  const definition = fieldDefinition(context.blueprint, field);
  return definition === undefined ? rawText(raw) : customValueText(definition, raw, context.members);
}

// 說明與長文字欄位只記「有改」，不記內容。
function isContentField(field: string, definition: FieldDefinition | undefined): boolean {
  return field === 'description' || definition?.type === 'long_text';
}

/** 一個欄位的修改說成一句話：改成、設為、清除、勾選，或只說「修改了」。 */
export function describeChange(change: FieldChange, context: ActivityContext): ChangeDescription {
  const definition = fieldDefinition(context.blueprint, change.field);
  const label = fieldLabel(context.blueprint, change.field);
  const quoted = `「${label}」`;
  if (definition?.type === 'checkbox') {
    return { label, segments: [text(change.to === true ? `勾選了${quoted}` : `取消勾選${quoted}`)] };
  }
  const from = valueText(change.field, change.from, context);
  const to = valueText(change.field, change.to, context);
  if (isContentField(change.field, definition) || (from === null && to === null)) {
    return { label, segments: [text(`修改了${quoted}`)] };
  }
  if (from === null) {
    return { label, segments: [text(`把${quoted}設為 `), value(to ?? '')] };
  }
  if (to === null) {
    return { label, segments: [text(`清除了${quoted}（原本是 `), value(from), text('）')] };
  }
  return { label, segments: [text(`把${quoted}從 `), value(from), text(' 改成 '), value(to)] };
}

export function changeText(description: ChangeDescription): string {
  return description.segments.map((segment) => segment.text).join('');
}

/** 附件的活動（PM-5）：changes 一筆、field 是 attachment，加上去的檔名在 to、刪掉的在 from。 */
const ATTACHMENT_FIELD = 'attachment';

/**
 * 「附加了檔案「規格.pdf」」「刪除了附件「規格.pdf」」。檔名不是連結：檔案可能已經刪了。
 * 沒帶檔名的紀錄（不該發生）只說前半句，不編一個名稱。
 */
export function attachmentDescription(activity: Activity): ChangeDescription {
  const isAdded = activity.kind === 'attachment_added';
  const lead = isAdded ? '附加了檔案' : '刪除了附件';
  const change = activity.changes.find((candidate) => candidate.field === ATTACHMENT_FIELD);
  const raw = isAdded ? change?.to : change?.from;
  const label = '附件';
  if (raw === undefined || raw === null) {
    return { label, segments: [text(lead)] };
  }
  return { label, segments: [text(`${lead}「`), value(rawText(raw)), text('」')] };
}

/** 同一個人連續附加檔案時，相隔多久以內併成一行（只在畫的時候併，資料與 API 不變）。 */
const ATTACHMENT_GROUP_WINDOW_MS = 5 * 60 * 1000;
/** 併成一行時列出幾個檔名，其餘寫「和其他 N 個」。 */
const GROUP_LISTED_NAMES = 3;

function attachmentNameOf(activity: Activity): string | null {
  const raw = activity.changes.find((candidate) => candidate.field === ATTACHMENT_FIELD)?.to;
  return raw === undefined || raw === null ? null : rawText(raw);
}

function continuesRun(previous: Activity | undefined, next: Activity): boolean {
  if (previous?.kind !== 'attachment_added' || next.kind !== 'attachment_added') return false;
  const gap = Date.parse(next.at) - Date.parse(previous.at);
  return previous.actor.personId === next.actor.personId && gap >= 0 && gap <= ATTACHMENT_GROUP_WINDOW_MS;
}

/**
 * 把活動分組：同一個人、連續、（和前一筆）相隔 5 分鐘內的 attachment_added 一組，其他的各自一組。
 * 傳進來的要是畫在同一層、照時間排好的活動（回覆不算，它們在留言底下）。
 */
export function groupAttachmentRuns(entries: readonly Activity[]): readonly (readonly Activity[])[] {
  return entries.reduce<readonly (readonly Activity[])[]>((groups, entry) => {
    const last = groups.at(-1);
    return last !== undefined && continuesRun(last.at(-1), entry) ? [...groups.slice(0, -1), [...last, entry]] : [...groups, [entry]];
  }, []);
}

/** 一組附加檔案：「附加了 3 個檔案：a.png、b.png、c.png」，太多時「…c.png 和其他 2 個」。只有一筆時同單筆的說法。 */
export function attachmentGroupDescription(group: readonly Activity[]): ChangeDescription {
  const [first] = group;
  if (group.length < 2 || first === undefined) return first === undefined ? { label: '附件', segments: [text('附加了檔案')] } : attachmentDescription(first);
  const names = group.map(attachmentNameOf).filter((name): name is string => name !== null);
  const listed = names.slice(0, GROUP_LISTED_NAMES);
  const lead = `附加了 ${String(group.length)} 個檔案`;
  if (listed.length === 0) return { label: '附件', segments: [text(lead)] };
  const rest = group.length - listed.length;
  const segments = listed.flatMap((name, index) => (index === 0 ? [value(name)] : [text('、'), value(name)]));
  return { label: '附件', segments: [text(`${lead}：`), ...segments, ...(rest > 0 ? [text(` 和其他 ${String(rest)} 個`)] : [])] };
}

/** 一組附加檔案的報讀摘要：人加上同一句話（太長照一般的規則截短）。 */
export function attachmentGroupAnnouncement(group: readonly Activity[]): string {
  const [first] = group;
  return `${first?.actor.displayName ?? ''} ${shorten(changeText(attachmentGroupDescription(group)), ANNOUNCED_ACTION_LENGTH)}`;
}

export function stateName(context: ActivityContext, stateKey: string | null): string {
  if (stateKey === null) {
    return '';
  }
  return findState(context.blueprint, context.typeKey, stateKey)?.name ?? stateKey;
}

/** 改狀態的說法：「把狀態從「待辦」改成「進行中」」。 */
export function transitionText(activity: Activity, context: ActivityContext): string {
  return `把狀態從「${stateName(context, activity.fromState)}」改成「${stateName(context, activity.toState)}」`;
}

/** 套用設定變更時的開頭：「套用設定變更（第 4 版）時」。 */
export function migrationLead(activity: Activity): string {
  const version = activity.blueprintVersion === null ? '' : `（第 ${String(activity.blueprintVersion)} 版）`;
  return `套用設定變更${version}時`;
}

/**
 * 套用時一個欄位被動到的說法：刪除的欄位「被封存」（值還在，只是不再顯示），換了型別的「從…轉成…」。
 * 刪除的欄位已經不在目前的藍圖裡，名稱與值的格式只能退回 key 與原始的值。
 */
function describeMigratedChange(change: FieldChange, context: ActivityContext): readonly Segment[] {
  const quoted = `「${fieldLabel(context.blueprint, change.field)}」`;
  const from = valueText(change.field, change.from, context);
  if (change.to === null) {
    return from === null ? [text(`${quoted}被封存`)] : [text(`${quoted}被封存（原本是 `), value(from), text('）')];
  }
  const to = valueText(change.field, change.to, context) ?? '';
  return [text(`${quoted}從 `), value(from ?? '空白'), text(' 轉成 '), value(to)];
}

/** 套用時這個項目被動到的每一件事：狀態搬移在前，再來是每個欄位。 */
export function migrationParts(activity: Activity, context: ActivityContext): readonly (readonly Segment[])[] {
  const move =
    activity.fromState === null && activity.toState === null
      ? []
      : [[text(`狀態從「${stateName(context, activity.fromState)}」搬到「${stateName(context, activity.toState)}」`)]];
  return [...move, ...activity.changes.map((change) => describeMigratedChange(change, context))];
}

function migrationText(activity: Activity, context: ActivityContext): string {
  const parts = migrationParts(activity, context);
  const [only] = parts;
  if (parts.length === 1 && only !== undefined) {
    return `${migrationLead(activity)}，${only.map((segment) => segment.text).join('')}`;
  }
  return `${migrationLead(activity)}，調整了 ${String(parts.length)} 件事`;
}

const ITEM_TYPE = 'item_type';

/** 改類型那一筆的前後類型；不是改類型（或沒有 item_type 那一筆）是 undefined。 */
function typeChangeOf(activity: Activity): { readonly from: string; readonly to: string } | undefined {
  if (activity.kind !== 'type_changed') return undefined;
  const change = activity.changes.find((candidate) => candidate.field === ITEM_TYPE);
  return change === undefined ? undefined : { from: String(change.from), to: String(change.to) };
}

/**
 * 每一筆活動當時的類型（以活動的編號為鍵），改狀態與搬移的狀態名稱要看當時那個類型的流程。
 * 從已載入的改類型往回推：在某筆改類型之前用它的 from，之後用它的 to（後面的頁還沒載入時，這比項目現在的類型準）；
 * 一筆改類型都沒載入時用項目現在的類型。改類型那一筆本身記的是改之前的類型（它的說明自己看前後兩個類型）。
 */
export function typesAtEntries(entries: readonly Activity[], currentType: string): ReadonlyMap<string, string> {
  const first = entries.map(typeChangeOf).find((change) => change !== undefined);
  let typeAt = first?.from ?? currentType;
  return new Map(
    entries.map((entry) => {
      const pair = [entry.id, typeAt] as const;
      typeAt = typeChangeOf(entry)?.to ?? typeAt;
      return pair;
    }),
  );
}

/** 狀態的名稱，看指定類型的流程；類型或狀態已經不在目前的藍圖裡時用 key。 */
function stateNameOf(blueprint: ProjectBlueprint, typeKey: string, stateKey: string): string {
  return findState(blueprint, typeKey, stateKey)?.name ?? stateKey;
}

/** 改類型時封存的一個欄位：有值的說原本是什麼；長文字只記「有改」，不記內容。 */
function archivedField(change: FieldChange, context: ActivityContext): readonly Segment[] {
  const quoted = `「${fieldLabel(context.blueprint, change.field)}」`;
  const from = valueText(change.field, change.from, context);
  return from === null ? [text(quoted)] : [text(`${quoted}（原本是 `), value(from), text('）')];
}

/**
 * 改類型的說法：「把類型從「需求」改成「任務」，狀態從「開發」搬到「進行中」；「報價金額」（原本是 $1,200）的值封存起來」。
 * 舊狀態在舊類型的流程裡、新狀態在新類型的流程裡，各自找名稱：之後又改過類型的話，項目現在的類型兩邊都對不上。
 */
export function typeChangeDescription(activity: Activity, context: ActivityContext): ChangeDescription {
  const typeChange = activity.changes.find((change) => change.field === ITEM_TYPE);
  const fromType = String(typeChange?.from ?? '');
  const toType = String(typeChange?.to ?? '');
  const lead = text(`把類型從「${itemTypeName(context.blueprint, fromType)}」改成「${itemTypeName(context.blueprint, toType)}」`);
  const move =
    activity.fromState === null || activity.toState === null
      ? []
      : [
          text(
            `，狀態從「${stateNameOf(context.blueprint, fromType, activity.fromState)}」搬到「${stateNameOf(context.blueprint, toType, activity.toState)}」`,
          ),
        ];
  const archived = activity.changes.filter((change) => change !== typeChange);
  const fields = archived.flatMap((change, index) => [...(index === 0 ? [] : [text('、')]), ...archivedField(change, context)]);
  const tail = archived.length === 0 ? [] : [text('；'), ...fields, text('的值封存起來')];
  return { label: fieldLabel(context.blueprint, 'type'), segments: [lead, ...move, ...tail] };
}

const PARENT = 'parent';

/**
 * 復原刪除的項目的說法：「復原了這個項目，狀態從「review」搬到「需求確認」；「報價金額」（原本是 $1,200）的值封存起來；拿掉上層 AGY-3」。
 * 搬移是復原時照最新的藍圖整理出來的，沒有搬移就只有第一句。狀態的名稱看復原當時的類型（context.typeKey）；
 * 封存的欄位寫法和改類型一樣；拿掉上層是 field 為 parent 的一筆，from 是上層的編號。
 */
export function restoreDescription(activity: Activity, context: ActivityContext): ChangeDescription {
  const move =
    activity.fromState === null || activity.toState === null
      ? []
      : [text(`，狀態從「${stateName(context, activity.fromState)}」搬到「${stateName(context, activity.toState)}」`)];
  const archived = activity.changes.filter((change) => change.field !== PARENT);
  const fields = archived.flatMap((change, index) => [...(index === 0 ? [] : [text('、')]), ...archivedField(change, context)]);
  const archivedPart = archived.length === 0 ? [] : [text('；'), ...fields, text('的值封存起來')];
  const parentChange = activity.changes.find((change) => change.field === PARENT);
  const parentPart = parentChange === undefined ? [] : [text('；拿掉上層 '), value(String(parentChange.from))];
  return {
    label: fieldLabel(context.blueprint, PARENT),
    segments: [text('復原了這個項目'), ...move, ...archivedPart, ...parentPart],
  };
}

/** 報讀新的活動時唸到這麼長就好：留言只唸開頭，其他的句子（例如很長的標題）也截短。 */
const ANNOUNCED_COMMENT_LENGTH = 40;
const ANNOUNCED_ACTION_LENGTH = 80;

const graphemes = new Intl.Segmenter('zh-TW', { granularity: 'grapheme' });

/** 換行與連續空白當成一個空白，超過 max 個字（以使用者看到的字為單位，表情符號不會被切開）就截斷。 */
function shorten(content: string, max: number): string {
  const flat = content.replace(/\s+/g, ' ').trim();
  const characters = Array.from(graphemes.segment(flat), (part) => part.segment);
  return characters.length <= max ? flat : `${characters.slice(0, max).join('')}…`;
}

function announcedAction(activity: Activity, context: ActivityContext): string {
  switch (activity.kind) {
    case 'created':
      return '建立了這個項目';
    case 'transitioned':
      return transitionText(activity, context);
    case 'commented':
      // 刪除的留言內容是 null：不唸「留言：」加空字串。
      return activity.comment === null ? '留言（已刪除）' : `留言：${shorten(activity.comment, ANNOUNCED_COMMENT_LENGTH)}`;
    case 'updated': {
      const [only] = activity.changes;
      return activity.changes.length === 1 && only !== undefined
        ? changeText(describeChange(only, context))
        : `修改了 ${String(activity.changes.length)} 個欄位`;
    }
    case 'migrated':
      return migrationText(activity, context);
    case 'type_changed':
      return changeText(typeChangeDescription(activity, context));
    case 'deleted':
      return DELETED_TEXT;
    case 'restored':
      return changeText(restoreDescription(activity, context));
    case 'attachment_added':
    case 'attachment_removed':
      return changeText(attachmentDescription(activity));
  }
}

/** 時間軸多了一筆時給螢幕報讀器的簡短摘要（人加上做了什麼）；完整的內容在時間軸上。 */
export function activityAnnouncement(activity: Activity, context: ActivityContext): string {
  return `${activity.actor.displayName} ${shorten(announcedAction(activity, context), ANNOUNCED_ACTION_LENGTH)}`;
}
