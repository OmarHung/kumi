import type { QueryClient } from '@tanstack/react-query';
import type { SettingsSection } from '../work-items/itemLinks';
import type { EditSession } from './useEditSession';

/**
 * 按「預覽變更」送出時，設定頁上的整份草稿：基準的藍圖與版本、每個區塊的修改（包含刪除狀態與類型換流程選的搬移目標）、
 * 摘要，以及當時在哪個區塊。預覽頁的「回去修改」靠它把草稿帶回設定頁。
 */
export interface ProposedDraft {
  readonly personId: string;
  readonly projectKey: string;
  readonly session: EditSession;
  readonly summary: string;
  readonly section: SettingsSection;
}

interface ShelvedDraft extends ProposedDraft {
  /** 這份草稿送出成的變更集；按了「回去修改」、等設定頁接回去時是 null。 */
  readonly changeSetNumber: number | null;
}

type Shelf = Map<string, ShelvedDraft>;

/**
 * 只放在記憶體：重新整理就沒了（可以接受，交接文件 U7），不放 sessionStorage——藍圖很大，而且這是一整份設定。
 * 不放進查詢的快取：快取會被失效、回收，草稿不是伺服器的資料。每個 QueryClient 一份：正式環境只有一個，
 * 測試每次 renderApp 一個，上一個測試送出的草稿不會跑到下一個測試。
 */
const shelves = new WeakMap<QueryClient, Shelf>();

function shelfOf(client: QueryClient): Shelf {
  const existing = shelves.get(client);
  if (existing !== undefined) return existing;
  const created: Shelf = new Map();
  shelves.set(client, created);
  return created;
}

/** 以專案代號加基準版本為鍵：別人套用了新版本之後，舊版本的草稿對不上新的版本，不會悄悄套在新的藍圖上。 */
function keyOf(projectKey: string, baseVersion: number): string {
  return `${projectKey}@${String(baseVersion)}`;
}

/** 設定頁送出成功：記下這份草稿與它變成的變更集。同一個專案只留最新的一份（再預覽一次就換成新的變更集）。 */
export function rememberProposedDraft(client: QueryClient, draft: ProposedDraft, changeSetNumber: number): void {
  forgetProjectDrafts(client, draft.projectKey);
  shelfOf(client).set(keyOf(draft.projectKey, draft.session.baseVersion), { ...draft, changeSetNumber });
}

/** 這份變更集是不是這個人剛從設定頁送出、草稿還在記憶體裡的那一份（預覽頁決定有沒有「回去修改」）。 */
export function hasProposedDraft(client: QueryClient, projectKey: string, baseVersion: number, changeSetNumber: number, personId: string): boolean {
  const draft = shelfOf(client).get(keyOf(projectKey, baseVersion));
  return draft !== undefined && draft.changeSetNumber === changeSetNumber && draft.personId === personId;
}

/**
 * 「回去修改」放棄了變更集之後：草稿改成等設定頁接回去，回傳送出時所在的區塊。
 * 記憶體裡不是這份變更集的草稿時回 null，什麼都不動。
 */
export function releaseProposedDraft(client: QueryClient, projectKey: string, baseVersion: number, changeSetNumber: number): SettingsSection | null {
  const shelf = shelfOf(client);
  const key = keyOf(projectKey, baseVersion);
  const draft = shelf.get(key);
  if (draft?.changeSetNumber !== changeSetNumber) return null;
  shelf.set(key, { ...draft, changeSetNumber: null });
  return draft.section;
}

/** 設定頁掛上時要接回去的草稿：只有按過「回去修改」的才算，還在等待核准的不會自己跑回來。 */
export function draftToResume(client: QueryClient, projectKey: string, personId: string): ProposedDraft | undefined {
  return [...shelfOf(client).values()].find(
    (draft) => draft.projectKey === projectKey && draft.changeSetNumber === null && draft.personId === personId,
  );
}

/** 設定頁接回去之後拿掉：草稿已經在設定頁上了，之後捨棄或離開，再進設定頁就從目前的設定開始。 */
export function forgetDraft(client: QueryClient, draft: ProposedDraft): void {
  const shelf = shelfOf(client);
  const key = keyOf(draft.projectKey, draft.session.baseVersion);
  if (shelf.get(key) === draft) shelf.delete(key);
}

/** 變更集套用或放棄之後：那一份的草稿不再需要。 */
export function forgetChangeSetDraft(client: QueryClient, projectKey: string, baseVersion: number, changeSetNumber: number): void {
  const shelf = shelfOf(client);
  const key = keyOf(projectKey, baseVersion);
  if (shelf.get(key)?.changeSetNumber === changeSetNumber) shelf.delete(key);
}

/** 登出（包含工作階段失效）：共用電腦上，下一個登入的人不能拿到上一個人的設定草稿（auth/session.ts 的 markSignedOut）。 */
export function forgetAllDrafts(client: QueryClient): void {
  shelves.delete(client);
}

/** 設定頁「捨棄修改」：這個專案記下的草稿都不要了。 */
export function forgetProjectDrafts(client: QueryClient, projectKey: string): void {
  const shelf = shelfOf(client);
  for (const [key, draft] of [...shelf.entries()]) {
    if (draft.projectKey === projectKey) shelf.delete(key);
  }
}
