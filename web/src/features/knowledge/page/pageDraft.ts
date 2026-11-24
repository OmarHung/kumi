import type { KnowledgePage, KnowledgePagePatch } from '../../../api/knowledgeTypes';
import { draftKey } from '../../../lib/draftStorage';

/** 編輯中的一頁：標題與內文一起打、一起存成草稿（只存內文的話，換頁回來標題就不見了）。 */
export interface PageDraft {
  readonly title: string;
  readonly body: string;
}

export function pageDraftOf(page: Pick<KnowledgePage, 'title' | 'body'>): PageDraft {
  return { title: page.title, body: page.body };
}

/** 草稿存成一段文字（useDraft 存的是字串）：固定的欄位順序，伺服器上的內容序列化之後才拿得來比對有沒有改。 */
export function serializePageDraft(draft: PageDraft): string {
  return JSON.stringify({ title: draft.title, body: draft.body });
}

/** 還原草稿：壞掉或不是這個形狀的內容當成沒有草稿，不讓編輯器帶著亂碼打開。 */
export function parsePageDraft(text: string | null): PageDraft | null {
  if (text === null) return null;
  try {
    const parsed: unknown = JSON.parse(text);
    if (typeof parsed === 'object' && parsed !== null && 'title' in parsed && 'body' in parsed && typeof parsed.title === 'string' && typeof parsed.body === 'string') {
      return { title: parsed.title, body: parsed.body };
    }
  } catch {
    // 不是我們存的格式：當成沒有草稿。
  }
  return null;
}

/** 鍵帶空間與頁面編號：每一頁各一份草稿（lib/draftStorage.ts）。 */
export function pageDraftKey(spaceKey: string, pageNumber: number): string {
  return draftKey('knowledge-page', `${spaceKey}.${String(pageNumber)}`);
}

/** 相對於 base 有改的欄位；兩個都沒改是 null（不送請求）。標題頭尾的空白由伺服器處理，這裡照使用者打的比。 */
export function patchOf(draft: PageDraft, base: PageDraft, expectedRevision: number): KnowledgePagePatch | null {
  const hasTitle = draft.title !== base.title;
  const hasBody = draft.body !== base.body;
  if (!hasTitle && !hasBody) return null;
  return { ...(hasTitle ? { title: draft.title } : {}), ...(hasBody ? { body: draft.body } : {}), expectedRevision };
}
