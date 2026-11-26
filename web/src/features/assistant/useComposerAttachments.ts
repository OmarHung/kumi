import { useEffect, useRef, useState, type Dispatch, type RefObject, type SetStateAction } from 'react';
import type { AssistantAttachment, AssistantAttachmentKind } from '../../api/aiTypes';
import { deleteAssistantAttachment, uploadAssistantAttachment } from '../../api/assistantAttachments';
import { errorMessage } from '../../api/http';
import { guessKind, rejectionReason } from './attachmentRules';

/** uploading：傳送中。ready：伺服器收下了，等著送出。failed：上傳失敗（可以重試）。rejected：前端先擋下（不上傳，只能移除）。 */
export type AttachmentStatus = 'uploading' | 'ready' | 'failed' | 'rejected';

export interface AttachmentEntry {
  readonly id: string;
  readonly file: File;
  readonly status: AttachmentStatus;
  /** 上傳成功前依類型與副檔名猜的；成功之後以伺服器看內容判斷的為準。 */
  readonly kind: AssistantAttachmentKind | null;
  readonly attachment: AssistantAttachment | null;
  /** failed、rejected 時給人看的原因。 */
  readonly message: string | null;
  /** 圖片的縮圖（本機的 object URL，移除或送出時 revoke）。 */
  readonly previewUrl: string | null;
}

const NEW_KEY = '';

interface State {
  readonly key: string;
  readonly byKey: Readonly<Record<string, readonly AttachmentEntry[]>>;
}

function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

function previewOf(file: File): string | null {
  if (!file.type.startsWith('image/') || typeof URL.createObjectURL !== 'function') return null;
  return URL.createObjectURL(file);
}

function revoke(url: string | null): void {
  if (url !== null && typeof URL.revokeObjectURL === 'function') URL.revokeObjectURL(url);
}

/** 換了對話：從「新的對話」送出而開出來的那一則，小卡跟著過去（和草稿的字一樣）；其他的各自一份。 */
function switchedTo(state: State, key: string, createdId: string | null): State {
  const carried = state.key === NEW_KEY && key === createdId ? (state.byKey[NEW_KEY] ?? []) : null;
  if (carried === null || carried.length === 0) return { ...state, key };
  return { key, byKey: { ...state.byKey, [NEW_KEY]: [], [key]: [...(state.byKey[key] ?? []), ...carried] } };
}

function mapEntries(state: State, change: (entries: readonly AttachmentEntry[]) => readonly AttachmentEntry[]): State {
  const byKey: Record<string, readonly AttachmentEntry[]> = {};
  for (const [name, entries] of Object.entries(state.byKey)) byKey[name] = change(entries);
  return { ...state, byKey };
}

const NO_ENTRIES: readonly AttachmentEntry[] = [];

function patched(state: State, id: string, patch: Partial<AttachmentEntry>): State {
  return mapEntries(state, (entries) => entries.map((entry) => (entry.id === id ? { ...entry, ...patch } : entry)));
}

export interface ComposerAttachments {
  readonly entries: readonly AttachmentEntry[];
  /** 選檔、拖進來、貼上：先擋數量與大小，其他的立刻開始上傳。 */
  readonly add: (files: readonly File[]) => void;
  /** 移除一張小卡：上傳中的中止、已經上傳的呼叫 DELETE（失敗不擋）。 */
  readonly remove: (id: string) => void;
  readonly retry: (id: string) => void;
  /** 伺服器收下了：這些小卡拿掉（檔案已經綁在訊息上，不能 DELETE）。 */
  readonly clearSent: (ids: readonly string[]) => void;
}

type SetState = Dispatch<SetStateAction<State>>;

/** 新選的檔案變成小卡：依序檢查數量與大小（算進已經接受的），被擋下的不上傳、不做縮圖。 */
function buildEntries(files: readonly File[], existing: readonly AttachmentEntry[], nextId: () => string, previews: Set<string>): readonly AttachmentEntry[] {
  const accepted = existing.filter((entry) => entry.status !== 'rejected');
  let count = accepted.length;
  let bytes = accepted.reduce((sum, entry) => sum + entry.file.size, 0);
  return files.map<AttachmentEntry>((file) => {
    const reason = rejectionReason(file, count, bytes);
    if (reason === null) {
      count += 1;
      bytes += file.size;
    }
    const previewUrl = reason === null ? previewOf(file) : null;
    if (previewUrl !== null) previews.add(previewUrl);
    return { id: nextId(), file, status: reason === null ? 'uploading' : 'rejected', kind: guessKind(file), attachment: null, message: reason, previewUrl };
  });
}

/** 一個檔案的上傳：進行中的請求記在 controllers（移除時中止），結果寫回那一張小卡。 */
function useUpload(projectKey: string, setState: SetState, controllers: RefObject<Map<string, AbortController>>) {
  return (entry: AttachmentEntry) => {
      const controller = new AbortController();
      controllers.current.set(entry.id, controller);
      setState((current) => patched(current, entry.id, { status: 'uploading', message: null }));
      uploadAssistantAttachment(projectKey, entry.file, { signal: controller.signal })
        .then((attachment) => {
          controllers.current.delete(entry.id);
          setState((current) => patched(current, entry.id, { status: 'ready', attachment, kind: attachment.kind }));
        })
        .catch((error: unknown) => {
          controllers.current.delete(entry.id);
          if (isAbort(error)) return;
          setState((current) => patched(current, entry.id, { status: 'failed', message: errorMessage(error) }));
        });
  };
}

/** 離開時中止還沒傳完的、釋放縮圖。 */
function useCleanup(controllers: RefObject<Map<string, AbortController>>, previews: RefObject<Set<string>>) {
  useEffect(() => {
    const active = controllers.current;
    const urls = previews.current;
    return () => {
      active.forEach((controller) => {
        controller.abort();
      });
      active.clear();
      urls.forEach(revoke);
      urls.clear();
    };
  }, [controllers, previews]);
}

/**
 * 輸入框的附件小卡，每個對話一份（和草稿的字一起分開，從「新的對話」送出時跟著過去）。
 * 選了就立刻上傳（先上傳、送出時只帶編號）。
 */
export function useComposerAttachments(projectKey: string, conversationId: string | undefined, createdId: string | null): ComposerAttachments {
  const key = conversationId ?? NEW_KEY;
  const [state, setState] = useState<State>({ key, byKey: {} });
  if (state.key !== key) setState(switchedTo(state, key, createdId));
  const entries = state.byKey[key] ?? NO_ENTRIES;

  const controllers = useRef(new Map<string, AbortController>());
  const previews = useRef(new Set<string>());
  const nextId = useRef(0);
  const upload = useUpload(projectKey, setState, controllers);
  useCleanup(controllers, previews);

  const add = (files: readonly File[]) => {
    const added = buildEntries(files, entries, () => `attachment-${String((nextId.current += 1))}`, previews.current);
    if (added.length === 0) return;
    setState((current) => ({ ...current, byKey: { ...current.byKey, [current.key]: [...(current.byKey[current.key] ?? NO_ENTRIES), ...added] } }));
    added.filter((entry) => entry.status === 'uploading').forEach(upload);
  };

  const forget = (ids: readonly string[]) => {
    for (const id of ids) {
      controllers.current.get(id)?.abort();
      controllers.current.delete(id);
    }
    entries.filter((entry) => ids.includes(entry.id)).forEach((entry) => {
      revoke(entry.previewUrl);
      if (entry.previewUrl !== null) previews.current.delete(entry.previewUrl);
    });
    setState((current) => mapEntries(current, (list) => list.filter((entry) => !ids.includes(entry.id))));
  };

  const remove = (id: string) => {
    const entry = entries.find((candidate) => candidate.id === id);
    // 移除失敗不擋：伺服器上待送的附件 24 小時後自己清掉。
    if (entry?.attachment != null) deleteAssistantAttachment(projectKey, entry.attachment.id).catch(() => undefined);
    forget([id]);
  };

  const retry = (id: string) => {
    const entry = entries.find((candidate) => candidate.id === id);
    if (entry?.status === 'failed') upload(entry);
  };

  return { entries, add, remove, retry, clearSent: forget };
}
