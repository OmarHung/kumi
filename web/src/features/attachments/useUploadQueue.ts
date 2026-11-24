import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import type { Attachment, UploadOptions } from '../../api/attachments';
import { errorMessage } from '../../api/http';

/** 佇列與編輯器要用到的附件欄位：項目的 Attachment 與知識庫頁面的 KnowledgeAttachment 都符合。 */
export interface UploadedFile {
  readonly id: string;
  readonly fileName: string;
  readonly contentType: string;
}

/** 傳一個檔案：由呼叫端決定傳到哪裡（項目或知識庫頁面）。 */
export type UploadFn<T extends UploadedFile = Attachment> = (file: File, options: UploadOptions) => Promise<T>;

/** 同時最多傳幾個，其他排隊（瀏覽器對同一個網域本來就有連線數的限制，一次全送反而每個都慢）。 */
export const MAX_PARALLEL_UPLOADS = 3;

/**
 * queued：排隊中。uploading：傳送中。failed：失敗，等使用者重試或移除。
 * done：伺服器收下了，附件在 attachment；清單還沒重抓到之前，畫面用它畫正式的那一列，換的時候不跳動。
 */
export type UploadStatus = 'queued' | 'uploading' | 'failed' | 'done';

export interface UploadEntry<T extends UploadedFile = Attachment> {
  readonly id: string;
  readonly file: File;
  readonly status: UploadStatus;
  readonly sent: number;
  /** 瀏覽器不知道總數時是 null。 */
  readonly total: number | null;
  /** failed 時的原因（給人看的）。 */
  readonly message: string | null;
  /** done 時伺服器回的附件。 */
  readonly attachment: T | null;
}

type EntryPatch<T extends UploadedFile> = Partial<Omit<UploadEntry<T>, 'id' | 'file'>>;

function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

function patchEntry<T extends UploadedFile>(entries: readonly UploadEntry<T>[], id: string, patch: EntryPatch<T>): readonly UploadEntry<T>[] {
  return entries.map((entry) => (entry.id === id ? { ...entry, ...patch } : entry));
}

type SetEntries<T extends UploadedFile> = Dispatch<SetStateAction<readonly UploadEntry<T>[]>>;

interface RunUpload<T extends UploadedFile> {
  readonly upload: UploadFn<T>;
  readonly entry: UploadEntry<T>;
  readonly controller: AbortController;
  readonly setEntries: SetEntries<T>;
  /** 請求結束（成功、失敗、取消）：不再算在傳送中的名額裡。 */
  readonly release: () => void;
  readonly onUploaded: (attachment: T, entryId: string, file: File) => void;
  /** 傳失敗（不含使用者取消）：給想知道是哪個檔案、為什麼的呼叫端；那一列照舊留在 failed。 */
  readonly onFailed?: (message: string, entryId: string, file: File) => void;
}

/** 送出一個檔案，進度、成功與失敗寫回那一列。取消是使用者自己按的（那一列已經拿掉了），不算失敗。 */
function runUpload<T extends UploadedFile>({ upload, entry, controller, setEntries, release, onUploaded, onFailed }: RunUpload<T>): void {
  setEntries((current) => patchEntry(current, entry.id, { status: 'uploading', sent: 0, total: null, message: null }));
  upload(entry.file, {
    signal: controller.signal,
    onProgress: (sent, total) => {
      setEntries((current) => patchEntry(current, entry.id, { sent, total }));
    },
  })
    .then((attachment) => {
      release();
      setEntries((current) => patchEntry(current, entry.id, { status: 'done', attachment }));
      onUploaded(attachment, entry.id, entry.file);
    })
    .catch((error: unknown) => {
      release();
      if (isAbort(error)) return;
      const message = errorMessage(error);
      setEntries((current) => patchEntry(current, entry.id, { status: 'failed', message }));
      onFailed?.(message, entry.id, entry.file);
    });
}

interface UploadQueueOptions<T extends UploadedFile> {
  readonly upload: UploadFn<T>;
  /** 一個檔案傳完了：呼叫端把它放進清單的快取、讓清單與活動重抓。 */
  readonly onUploaded: (attachment: T, entryId: string, file: File) => void;
  readonly onFailed?: (message: string, entryId: string, file: File) => void;
}

export interface UploadQueue<T extends UploadedFile = Attachment> {
  readonly entries: readonly UploadEntry<T>[];
  readonly add: (files: readonly File[]) => void;
  /** 取消傳送中或排隊的、移除失敗的：中止請求並拿掉那一列。 */
  readonly remove: (entryId: string) => void;
  readonly retry: (entryId: string) => void;
  /** 清單已經有這些附件了：畫面換成清單裡的，不再用 done 的那一列。 */
  readonly settle: (knownIds: ReadonlySet<string>) => void;
  /** 這個附件已經被刪掉了：傳完但還沒換掉的那一列也拿掉，不然它會一直留到離開。 */
  readonly forget: (attachmentId: string) => void;
}

/** 排隊中、還沒開始傳的，依序補滿同時傳送的名額。 */
function useStarter<T extends UploadedFile>(entries: readonly UploadEntry<T>[], setEntries: SetEntries<T>, callbacks: UploadQueueOptions<T>) {
  // 正在傳的請求：以它而不是 state 算「現在幾個」，StrictMode 把 effect 跑兩次時才不會多開。
  const running = useRef(new Map<string, AbortController>());
  const callbacksRef = useRef(callbacks);
  useEffect(() => {
    callbacksRef.current = callbacks;
  });
  useEffect(() => {
    const queued = entries.filter((entry) => entry.status === 'queued' && !running.current.has(entry.id));
    for (const entry of queued) {
      if (running.current.size >= MAX_PARALLEL_UPLOADS) break;
      const controller = new AbortController();
      running.current.set(entry.id, controller);
      const release = () => {
        running.current.delete(entry.id);
      };
      runUpload({
        upload: (...args) => callbacksRef.current.upload(...args),
        entry,
        controller,
        setEntries,
        release,
        onUploaded: (...args) => {
          callbacksRef.current.onUploaded(...args);
        },
        onFailed: (...args) => {
          callbacksRef.current.onFailed?.(...args);
        },
      });
    }
  }, [entries, setEntries]);
  // 離開這個項目（元件卸載）時中止還沒傳完的，不留在背景悄悄傳。
  useEffect(() => {
    const active = running.current;
    return () => {
      active.forEach((controller) => {
        controller.abort();
      });
      active.clear();
    };
  }, []);
  return running;
}

/** 上傳佇列：一個檔案一個請求，同時最多 MAX_PARALLEL_UPLOADS 個，其他排隊。 */
export function useUploadQueue<T extends UploadedFile = Attachment>({ upload, onUploaded, onFailed }: UploadQueueOptions<T>): UploadQueue<T> {
  const [entries, setEntries] = useState<readonly UploadEntry<T>[]>([]);
  const nextId = useRef(0);
  const running = useStarter(entries, setEntries, { upload, onUploaded, onFailed });

  const add = useCallback((files: readonly File[]) => {
    const added = files.map<UploadEntry<T>>((file) => {
      nextId.current += 1;
      return { id: `upload-${String(nextId.current)}`, file, status: 'queued', sent: 0, total: null, message: null, attachment: null };
    });
    setEntries((current) => [...current, ...added]);
  }, []);

  const remove = useCallback(
    (entryId: string) => {
      running.current.get(entryId)?.abort();
      running.current.delete(entryId);
      setEntries((current) => current.filter((entry) => entry.id !== entryId));
    },
    [running],
  );

  const retry = useCallback((entryId: string) => {
    setEntries((current) => patchEntry(current, entryId, { status: 'queued', sent: 0, total: null, message: null }));
  }, []);

  const settle = useCallback((knownIds: ReadonlySet<string>) => {
    setEntries((current) => {
      const next = current.filter((entry) => !(entry.status === 'done' && entry.attachment !== null && knownIds.has(entry.attachment.id)));
      return next.length === current.length ? current : next;
    });
  }, []);

  const forget = useCallback((attachmentId: string) => {
    setEntries((current) => current.filter((entry) => entry.attachment?.id !== attachmentId));
  }, []);

  return { entries, add, remove, retry, settle, forget };
}
