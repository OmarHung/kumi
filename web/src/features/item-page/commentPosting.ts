import { draftKey, readDraft, removeDraft } from '../../lib/draftStorage';

/**
 * 送出中換了頁又回來時，畫面上的留言框已經是另一個元件（以項目編號為 key 重新掛上），還從草稿還原了同一段字。
 * 送出的那一個元件可能已經不在了，所以「這個項目有留言在送」「送出成功了」要讓同一個項目現在掛著的留言框知道：
 * 前者用 TanStack Query 的 mutationKey（useIsMutating），後者用這裡的通知。
 */
export function commentMutationKey(itemKey: string, replyTo?: string): readonly unknown[] {
  // 回覆用另一個名字：useIsMutating 是前綴比對，同一個前綴會讓最上層的留言框也當成「送出中」。
  return replyTo === undefined ? ['item', itemKey, 'add-comment'] : ['item', itemKey, 'add-reply', replyTo];
}

interface PostedComment {
  readonly itemKey: string;
  /** 回覆的是哪一則最上層留言；最上層的留言是 undefined。 */
  readonly replyTo: string | undefined;
  /** 送出的內容（去掉前後空白之後）。 */
  readonly body: string;
}

type Listener = (posted: PostedComment) => void;

const listeners = new Set<Listener>();

/**
 * 留言送出成功。草稿還是剛送出的那一段才清掉（送出中回來又改了，改過的留著）；再通知同一個項目現在掛著的留言框。
 * 元件不在了也照樣執行：呼叫端放在 useMutation 的 onSuccess 上。
 */
export function announceCommentPosted(itemKey: string, body: string, replyTo?: string): void {
  const key = draftKey('comment', itemKey, replyTo);
  if (readDraft(key)?.trim() === body) removeDraft(key);
  listeners.forEach((listener) => {
    listener({ itemKey, replyTo, body });
  });
}

/** 這個項目（回覆就是那一串）的留言送出成功時呼叫 listener（帶著送出的內容）。回傳取消訂閱的函式。 */
export function onCommentPosted(itemKey: string, listener: (body: string) => void, replyTo?: string): () => void {
  const handle: Listener = (posted) => {
    if (posted.itemKey === itemKey && posted.replyTo === replyTo) listener(posted.body);
  };
  listeners.add(handle);
  return () => {
    listeners.delete(handle);
  };
}
