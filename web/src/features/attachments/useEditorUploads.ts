import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import type { Attachment } from '../../api/attachments';
import { growthOf, isInlineImageType, nextPlaceholder, pastedFileName, referenceOf, stripPlaceholders } from './editorUploadText';
import { useUploadQueue, type UploadedFile, type UploadFn, type UploadQueue } from './useUploadQueue';
import { useUploadReporter, type UploadReporter } from './useUploadReporter';

const BYTES_PER_MB = 1024 * 1024;

export const UPLOADING_REASON = '還有檔案在上傳';
const TEXT_TOO_LONG = '文字太長，放不下檔案的參照。';
export const STALE_PLACEHOLDERS_REMOVED = '有沒傳完的檔案已經移除，請重新加入。';

/** 拖進來的事件只用到這兩樣：外框的 React 合成事件與編輯器的原生 handleDrop 事件都符合。 */
interface DragEventLike {
  readonly dataTransfer: DataTransfer | null;
  preventDefault(): void;
}

/** 編輯器放記號、換記號的目標。 */
export interface UploadPlaceholderSpec {
  readonly placeholder: string;
  readonly name: string;
  readonly isImage: boolean;
}

export interface UploadTarget {
  /** 在游標（或選取的範圍）放上這些記號（每個一個 uploadPlaceholder，中間用 hardBreak 分開），游標放在後面。 */
  insertPlaceholders(placeholders: readonly UploadPlaceholderSpec[]): void;
  /** 把那個記號換成 replacement 的 Markdown（傳完的參照；空字串是拿掉）；找不到回傳 false。 */
  replacePlaceholder(placeholder: string, replacement: string): boolean;
}

/** 編輯器上要接的事件（貼上、拖進來）。 */
export interface EditorFileBinding {
  readonly handlers: {
    readonly onPaste: (event: ClipboardEvent) => void;
    readonly onDragEnter: (event: DragEventLike) => void;
    readonly onDragOver: (event: DragEventLike) => void;
    readonly onDragLeave: () => void;
    readonly onDrop: (event: DragEventLike) => void;
  };
  /** 編輯器掛上時註冊自己的目標、卸下時給 null；放記號與換記號都交給目標。註冊前選的檔案（編輯器還在下載）這時才加入。 */
  readonly attach: (target: UploadTarget | null) => void;
  /** 檔案正拖在編輯器上方（而且可以上傳）：框畫成焦點的外框。 */
  readonly isDragging: boolean;
  /** 可以上傳：編輯器下面的提示才說可以貼上或拖進檔案；不能上傳時事件照樣接，貼上或拖進來時說明原因。 */
  readonly canUpload: boolean;
}

export interface EditorUploads {
  /** 有 item.update 而且專案沒封存。沒有時不放「插入圖片或檔案」。 */
  readonly canUpload: boolean;
  /** 還有檔案排隊或傳送中：送出、儲存要停用。 */
  readonly isUploading: boolean;
  readonly binding: EditorFileBinding;
  /** 「插入圖片或檔案」選好的檔案。 */
  readonly addFiles: (files: readonly File[]) => void;
  /** 還有檔案在上傳時按了送出（Ctrl／⌘+Enter）：照樣不送，用即時區域說原因（按鈕上的原因是靜態的，報讀軟體不會自己唸）。 */
  readonly announceUploading: () => void;
  /** 沒傳成功或沒送出的原因（給人看的），編輯器下面顯示。 */
  readonly problems: readonly string[];
  readonly dismissProblems: () => void;
  /** 給報讀軟體的一句話（完成、失敗、被擋下）。 */
  readonly announcement: string;
  /**
   * 即時區域要不要掛上：一直掛著的話，每個留言框與說明都多一個 role="status"，頁面上的其他提示（已還原、已送出）
   * 就不再是唯一的即時區域；用到上傳（貼上、拖進來、選檔、被擋下）才掛，之後一直在。
   */
  readonly hasAnnouncer: boolean;
}

export interface EditorUploadsOptions<T extends UploadedFile = Attachment> {
  /** 傳一個檔案：項目的附件或知識庫頁面的附件，由呼叫端決定（編輯器本身不認得兩者的差別）。 */
  readonly upload: UploadFn<T>;
  /** 有權限上傳（項目：item.update 而且專案沒封存）。 */
  readonly canUpload: boolean;
  /** 一個檔案傳完了：呼叫端把它放進清單的快取、讓清單重抓。 */
  readonly onUploaded?: (attachment: T) => void;
  /** 沒有權限時貼上、拖進來、選檔要說的原因。 */
  readonly noPermissionMessage: string;
  /** 單一檔案的上限（位元組）：上傳前先擋。 */
  readonly maxBytes: number;
  readonly value: string;
  readonly onChange: (next: string) => void;
  readonly maxLength: number;
  /** 編輯器唯讀（送出中、儲存中）：貼上、拖進來、選檔一律忽略。 */
  readonly isReadOnly?: boolean;
}

function hasFiles(types: readonly string[] | DOMStringList): boolean {
  return Array.from(types).includes('Files');
}

/** 資料夾或讀不到的東西：沒有大小也沒有類型（和附件區一樣的判斷）。 */
function isUnreadable(file: File): boolean {
  return file.size === 0 && file.type === '';
}

interface Screened {
  readonly accepted: readonly File[];
  readonly problems: readonly string[];
}

function screenFiles(files: readonly File[], maxBytes: number): Screened {
  const unreadable = files.filter(isUnreadable).map((file) => `不是檔案或讀不到：${file.name}`);
  const oversized = files.filter((file) => !isUnreadable(file) && file.size > maxBytes).map((file) => `超過 ${String(maxBytes / BYTES_PER_MB)} MB：${file.name}`);
  return {
    accepted: files.filter((file) => !isUnreadable(file) && file.size <= maxBytes),
    problems: [...unreadable, ...oversized],
  };
}

/**
 * 貼上與拖進來：有檔案才接手（preventDefault），只有文字照常貼上；不能上傳時事件照樣接，交給 addFiles 說明原因。
 * 剪貼簿同時有文字與檔案（試算表、Word、網頁複製）時，使用者要的是文字：照常貼，不上傳。
 * namePasted：貼上的檔案要用的名稱（截圖改名）。
 */
function useFileBinding(
  addFiles: (files: readonly File[]) => void,
  canUpload: boolean,
  namePasted: (files: readonly File[]) => File[],
  attach: (target: UploadTarget | null) => void,
): EditorFileBinding {
  const [isDragging, setIsDragging] = useState(false);
  const enter = (event: DragEventLike) => {
    if (event.dataTransfer === null || !hasFiles(event.dataTransfer.types)) return;
    event.preventDefault();
    setIsDragging(true);
  };
  return {
    isDragging: isDragging && canUpload,
    canUpload,
    attach,
    handlers: {
      onPaste: (event) => {
        if (event.clipboardData === null) return;
        const files = Array.from(event.clipboardData.files);
        if (files.length === 0 || event.clipboardData.getData('text/plain') !== '') return;
        event.preventDefault();
        addFiles(namePasted(files));
      },
      onDragEnter: enter,
      onDragOver: enter,
      onDragLeave: () => {
        setIsDragging(false);
      },
      onDrop: (event) => {
        if (event.dataTransfer === null || !hasFiles(event.dataTransfer.types)) return;
        event.preventDefault();
        setIsDragging(false);
        addFiles(Array.from(event.dataTransfer.files));
      },
    },
  };
}

interface Tracked {
  readonly placeholder: string;
  /** 給人看的名稱：同名的第二個起帶序號，和記號一致。 */
  readonly label: string;
}

/** 擋掉不能傳的檔案，其餘的配上「上傳中」的記號（同名的記號加序號，分得開）；放在哪裡由編輯器的游標決定，這裡只估長度（當作加在最後面）。 */
function planInsertion(files: readonly File[], value: string, maxBytes: number, isTaken: (placeholder: string) => boolean) {
  const { accepted, problems } = screenFiles(files, maxBytes);
  const used = new Set<string>();
  const placeholders = new Map<File, Tracked>();
  accepted.forEach((file) => {
    const next = nextPlaceholder(file, (candidate) => used.has(candidate) || isTaken(candidate) || value.includes(candidate));
    used.add(next.placeholder);
    placeholders.set(file, next);
  });
  const inserted = `${value}${Array.from(placeholders.values(), (item) => item.placeholder).join('\n')}`;
  return { accepted, placeholders, inserted, problems };
}

/** 最新一次畫面的值：請求結束的回呼在畫面之後才跑，拿 ref 才不會用到舊的。 */
function useLatest<T>(value: T) {
  const ref = useRef(value);
  useEffect(() => {
    ref.current = value;
  });
  return ref;
}

interface PlaceholderQueueOptions<T extends UploadedFile> {
  readonly upload: UploadFn<T>;
  readonly onUploaded?: (attachment: T) => void;
  readonly value: string;
  readonly targetRef: RefObject<UploadTarget | null>;
  readonly reporter: UploadReporter;
}

/** 使用者把「上傳中」的記號刪掉了：那個上傳不必再傳，中止並拿掉記錄（送出不再被它擋住，完成時也不補回）。 */
function useAbortRemovedPlaceholders(value: string, tracked: RefObject<Map<File, Tracked>>, queueRef: RefObject<UploadQueue<UploadedFile> | null>) {
  useEffect(() => {
    tracked.current.forEach((item, file) => {
      if (value.includes(item.placeholder)) return;
      tracked.current.delete(file);
      const entry = queueRef.current?.entries.find((candidate) => candidate.file === file);
      if (entry !== undefined) queueRef.current?.remove(entry.id);
    });
  }, [value, tracked, queueRef]);
}

/**
 * 上傳的佇列加上文字裡的記號：傳完把記號換成參照（附件也放進清單的快取），失敗把記號拿掉並說明。
 * 記號已經被使用者刪掉時不補回去。
 */
function usePlaceholderQueue<T extends UploadedFile>({ upload, onUploaded, value, targetRef, reporter }: PlaceholderQueueOptions<T>) {
  const onUploadedRef = useLatest(onUploaded);
  const queueRef = useRef<UploadQueue<T> | null>(null);
  /** 每個檔案放進文字裡的記號。 */
  const tracked = useRef(new Map<File, Tracked>());
  const settle = (file: File, entryId: string, replacement: string) => {
    const item = tracked.current.get(file);
    // 找不到記號就是使用者已經刪掉了，不補回去；輸入法組字中先不換也是編輯器的事。編輯器已經卸下（沒有目標）時沒有地方可換。
    if (item !== undefined) targetRef.current?.replacePlaceholder(item.placeholder, replacement);
    tracked.current.delete(file);
    queueRef.current?.remove(entryId);
  };
  const queue = useUploadQueue({
    upload,
    onUploaded: (attachment, entryId, file) => {
      onUploadedRef.current?.(attachment);
      reporter.sayLater(`上傳完成：${tracked.current.get(file)?.label ?? attachment.fileName}`);
      settle(file, entryId, referenceOf(attachment));
    },
    onFailed: (message, entryId, file) => {
      const failure = `${tracked.current.get(file)?.label ?? file.name} 上傳失敗：${message}`;
      reporter.showProblems([failure]);
      reporter.sayLater(failure);
      settle(file, entryId, '');
    },
  });
  useEffect(() => {
    queueRef.current = queue;
  });
  useAbortRemovedPlaceholders(value, tracked, queueRef);
  return { queue, tracked };
}

/** 還原的草稿裡殘留的「上傳中」記號（上傳在換頁時已中止）：掛上時拿掉並說明。 */
function useRemoveStalePlaceholders(value: string, onChangeRef: RefObject<(next: string) => void>, reporter: UploadReporter) {
  const initial = useRef(value);
  const { showProblems, say } = reporter;
  useEffect(() => {
    const { value: clean, removed } = stripPlaceholders(initial.current);
    if (removed === 0) return;
    onChangeRef.current(clean);
    showProblems([STALE_PLACEHOLDERS_REMOVED]);
    say(STALE_PLACEHOLDERS_REMOVED);
  }, [onChangeRef, showProblems, say]);
}

/** 貼上的檔案名稱：沒有意義的圖片名稱（image.png）換成「截圖 日期 時間」，同一分鐘的不重複。 */
function useScreenshotNames() {
  const taken = useRef(new Set<string>());
  return useCallback((files: readonly File[]) => {
    const now = new Date();
    return files.map((file) => {
      const name = pastedFileName(file, now, taken.current);
      taken.current.add(name);
      return name === file.name ? file : new File([file], name, { type: file.type, lastModified: file.lastModified });
    });
  }, []);
}

/**
 * 編輯器註冊的目標。編輯器還在下載時使用者可能已經按了「插入圖片或檔案」：沒有目標時選的檔案先記著，
 * 目標註冊時照常加入（不丟掉，也不插進文字裡）。
 */
function useTargetRegistry(addFilesRef: RefObject<(files: readonly File[]) => void>) {
  const targetRef = useRef<UploadTarget | null>(null);
  const pendingRef = useRef<readonly File[]>([]);
  const attach = useCallback(
    (target: UploadTarget | null) => {
      targetRef.current = target;
      if (target === null || pendingRef.current.length === 0) return;
      const pending = pendingRef.current;
      pendingRef.current = [];
      addFilesRef.current(pending);
    },
    [addFilesRef],
  );
  const hold = useCallback((files: readonly File[]) => {
    pendingRef.current = [...pendingRef.current, ...files];
  }, []);
  return { targetRef, attach, hold };
}

/** 這一批能不能開始、要說什麼：傳完參照比記號長（多了編號），把還在傳的與新加的一起預留，不然送出才 422。 */
function outcomeOf(plan: ReturnType<typeof planInsertion>, tracked: ReadonlyMap<File, Tracked>, maxLength: number) {
  const growth = [...tracked, ...plan.placeholders].reduce((sum, [file, item]) => sum + growthOf(file, item.placeholder), 0);
  const isTooLong = plan.accepted.length > 0 && plan.inserted.length + growth > maxLength;
  const problems = isTooLong ? [...plan.problems, TEXT_TOO_LONG] : plan.problems;
  const isStarting = !isTooLong && plan.accepted.length > 0;
  const spoken = isStarting ? [...problems, `開始上傳 ${String(plan.accepted.length)} 個檔案`] : problems;
  return { problems, spoken, isStarting };
}

function specsOf(placeholders: ReadonlyMap<File, Tracked>): UploadPlaceholderSpec[] {
  return Array.from(placeholders, ([file, item]) => ({ placeholder: item.placeholder, name: item.label, isImage: isInlineImageType(file.type) }));
}

function useAnnounceUploading(say: (text: string) => void) {
  return useCallback(() => {
    say(UPLOADING_REASON);
  }, [say]);
}

/**
 * 留言、說明、專案說明的編輯器裡貼圖、附加檔案（PM-7，docs/design/08-interaction.md 8.3「貼圖與附加檔案」）：
 * 貼上、拖進來、「插入圖片或檔案」三條路都走 addFiles。檔案先在游標的位置放一個「上傳中」的記號，
 * 傳完換成 attachment: 的參照，失敗拿掉並說明。上傳的對象由呼叫端決定（項目的附件用 useItemEditorUploads；PM-5 的佇列：同時 3 個、25 MB），
 * 傳完讓附件清單與活動重抓。文字用 ref 拿最新的一份：傳完時使用者可能已經又打了字，不能用舊的值蓋掉。
 */
export function useEditorUploads<T extends UploadedFile = Attachment>(options: EditorUploadsOptions<T>): EditorUploads {
  const { upload, canUpload, onUploaded, noPermissionMessage, maxBytes, value, onChange, maxLength, isReadOnly = false } = options;
  const addFilesRef = useRef<(files: readonly File[]) => void>(() => undefined);
  const { targetRef, attach, hold } = useTargetRegistry(addFilesRef);
  const reporter = useUploadReporter();
  const { showProblems, say } = reporter;
  const valueRef = useLatest(value);
  const onChangeRef = useLatest(onChange);
  const { queue, tracked } = usePlaceholderQueue({ upload, onUploaded, value, targetRef, reporter });
  useRemoveStalePlaceholders(value, onChangeRef, reporter);

  const addFiles = useCallback(
    (files: readonly File[]) => {
      if (files.length === 0 || isReadOnly) return;
      if (!canUpload) {
        showProblems([noPermissionMessage]);
        say(noPermissionMessage);
        return;
      }
      const target = targetRef.current;
      if (target === null) {
        hold(files);
        return;
      }
      const plan = planInsertion(files, valueRef.current, maxBytes, (candidate) => Array.from(tracked.current.values()).some((item) => item.placeholder === candidate));
      const { problems, spoken, isStarting } = outcomeOf(plan, tracked.current, maxLength);
      showProblems(problems);
      if (spoken.length > 0) say(spoken.join('。'));
      if (!isStarting) return;
      plan.placeholders.forEach((item, file) => tracked.current.set(file, item));
      target.insertPlaceholders(specsOf(plan.placeholders));
      queue.add(plan.accepted);
    },
    [canUpload, hold, isReadOnly, maxBytes, maxLength, noPermissionMessage, queue, say, showProblems, targetRef, tracked, valueRef],
  );
  useEffect(() => {
    addFilesRef.current = addFiles;
  }, [addFiles]);

  return {
    canUpload,
    // 佇列裡還有的都算（傳送中，或傳完了但記號還沒換成參照）：失敗與換好的會被拿掉。
    isUploading: queue.entries.length > 0,
    binding: useFileBinding(addFiles, canUpload, useScreenshotNames(), attach),
    addFiles,
    announceUploading: useAnnounceUploading(say),
    problems: reporter.problems,
    dismissProblems: reporter.dismiss,
    announcement: reporter.announcement,
    hasAnnouncer: reporter.isStarted,
  };
}
