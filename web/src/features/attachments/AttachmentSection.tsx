import { useEffect, useId, useMemo, useRef, useState, type DragEvent, type ReactNode, type RefObject } from 'react';
import { ATTACHMENT_MAX_BYTES, type Attachment } from '../../api/attachments';
import type { ItemDetail } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { CountBadge } from '../../components/CountBadge/CountBadge';
import { Icon } from '../../components/Icon/Icon';
import { Notice } from '../../components/Notice/Notice';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { SectionHeading } from '../item-page/ItemPanelContext';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { AttachmentList, rowsOf, type AttachmentListProps } from './AttachmentList';
import { DeleteAttachmentDialog } from './DeleteAttachmentDialog';
import { useAttachmentCache, useAttachments } from './useAttachments';
import { useRemovalFocus, type RemovalFocus } from './useRemovalFocus';
import { useUploadAnnouncement } from './useUploadAnnouncement';
import { useUploadQueue } from './useUploadQueue';
import styles from './AttachmentSection.module.css';

const MAX_MB = ATTACHMENT_MAX_BYTES / (1024 * 1024);
const SKELETON_ROWS = 1;
const NO_ATTACHMENTS: readonly Attachment[] = [];

function hasFiles(event: DragEvent<HTMLElement>): boolean {
  return Array.from(event.dataTransfer.types).includes('Files');
}

/** 拖進來的東西裡的資料夾名稱（瀏覽器把資料夾也放在 files 裡，沒辦法當檔案讀）；不支援 webkitGetAsEntry 的環境是空的。 */
function directoryNames(event: DragEvent<HTMLElement>): readonly string[] {
  const items = event.dataTransfer.items as DataTransferItemList | undefined;
  const entries = Array.from(items ?? []);
  return entries.flatMap((item) => {
    const entry = typeof item.webkitGetAsEntry === 'function' ? item.webkitGetAsEntry() : null;
    return entry?.isDirectory === true ? [entry.name] : [];
  });
}

/** 資料夾或讀不到的東西：沒有大小也沒有類型。真的是空檔案又沒有副檔名的很少見，擋下來說明比送出後說「連不上伺服器」好。 */
function isUnreadable(file: File, directories: ReadonlySet<string>): boolean {
  return directories.has(file.name) || (file.size === 0 && file.type === '');
}

/**
 * 拖進來的檔案：拖曳進入、離開時的高亮，放開時交給 onFiles。只認檔案（拖一段文字經過不亮）。
 * 游標移到裡面的子元素也會觸發 dragleave：還在區域裡就不取消高亮，免得閃。
 */
function useFileDrop(isEnabled: boolean, onFiles: (files: readonly File[], directories: readonly string[]) => void) {
  const [isDragging, setIsDragging] = useState(false);
  if (!isEnabled) return { isDragging: false, handlers: {} };
  const enter = (event: DragEvent<HTMLElement>) => {
    if (!hasFiles(event)) return;
    event.preventDefault();
    setIsDragging(true);
  };
  return {
    isDragging,
    handlers: {
      onDragEnter: enter,
      onDragOver: enter,
      onDragLeave: (event: DragEvent<HTMLElement>) => {
        if (event.relatedTarget instanceof Node && event.currentTarget.contains(event.relatedTarget)) return;
        setIsDragging(false);
      },
      onDrop: (event: DragEvent<HTMLElement>) => {
        if (!hasFiles(event)) return;
        event.preventDefault();
        setIsDragging(false);
        onFiles(Array.from(event.dataTransfer.files), directoryNames(event));
      },
    },
  };
}

/** 一個名稱就一句話（「lead：名稱」），好幾個列成清單。 */
function namesMessage(lead: string, names: readonly string[]) {
  const [only] = names;
  if (names.length === 0) return null;
  if (names.length === 1 && only !== undefined) return `${lead}：${only}`;
  return (
    <>
      {`${lead}：`}
      <ul className={styles.rejected}>
        {names.map((name, index) => (
          // 不同資料夾的檔案可能同名；順序固定，用位置當 key。
          <li key={index}>{name}</li>
        ))}
      </ul>
    </>
  );
}

function oversizedMessage(names: readonly string[]) {
  if (names.length === 1) return namesMessage(`超過 ${String(MAX_MB)} MB`, names);
  return namesMessage(`超過 ${String(MAX_MB)} MB，沒有上傳`, names);
}

interface UploadButtonProps {
  readonly buttonRef: RefObject<HTMLButtonElement | null>;
  readonly onFiles: (files: readonly File[]) => void;
}

/** 「上傳檔案」：一直都在，拖曳不是唯一的方法。可以多選。 */
function UploadButton({ buttonRef, onFiles }: UploadButtonProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  return (
    <>
      <Button
        ref={buttonRef}
        size="sm"
        onClick={() => {
          inputRef.current?.click();
        }}
      >
        <Icon name="upload" />
        上傳檔案
      </Button>
      <input
        ref={inputRef}
        type="file"
        multiple
        hidden
        aria-label="選擇要上傳的檔案"
        onChange={(event) => {
          onFiles(Array.from(event.target.files ?? []));
          event.target.value = '';
        }}
      />
    </>
  );
}

/** 送出之前先擋下的檔案：超過上限的、資料夾或讀不到的。記下是哪幾個，其他的交給佇列。 */
function useFileIntake(add: (files: readonly File[]) => void) {
  const [rejected, setRejected] = useState<{ readonly oversized: readonly string[]; readonly unreadable: readonly string[] }>({ oversized: [], unreadable: [] });
  return {
    rejected,
    dismiss: () => {
      setRejected({ oversized: [], unreadable: [] });
    },
    addFiles: (files: readonly File[], directories: readonly string[] = []) => {
      const directorySet = new Set(directories);
      const readable = files.filter((file) => !isUnreadable(file, directorySet));
      const unreadable = [...files.filter((file) => isUnreadable(file, directorySet)).map((file) => file.name), ...directories.filter((name) => !files.some((file) => file.name === name))];
      setRejected({ oversized: readable.filter((file) => file.size > ATTACHMENT_MAX_BYTES).map((file) => file.name), unreadable });
      add(readable.filter((file) => file.size <= ATTACHMENT_MAX_BYTES));
    },
  };
}

/**
 * 清單的資料與上傳佇列湊在一起：伺服器上的附件加上這次上傳的列、列拿掉之後的焦點接手。
 * 上傳完成的回呼在請求結束時才跑，所以焦點的計畫用 ref 拿最新一次畫面的那一份。
 */
function useAttachmentModel(itemKey: string) {
  const listRef = useRef<HTMLDivElement>(null);
  const uploadButtonRef = useRef<HTMLButtonElement>(null);
  const focusRef = useRef<RemovalFocus | null>(null);
  const query = useAttachments(itemKey);
  const cache = useAttachmentCache(itemKey);
  const attachments = query.data ?? NO_ATTACHMENTS;
  const queue = useUploadQueue({
    itemKey,
    onUploaded: (attachment, entryId) => {
      focusRef.current?.planReplacement(entryId, attachment.id);
      cache.added(attachment);
    },
  });
  const rows = useMemo(() => rowsOf(attachments, queue.entries), [attachments, queue.entries]);
  const focus = useRemovalFocus(listRef, uploadButtonRef, rows.map((row) => row.id));
  useEffect(() => {
    focusRef.current = focus;
  });
  const { settle } = queue;
  useEffect(() => {
    settle(new Set(attachments.map((attachment) => attachment.id)));
  }, [attachments, settle]);
  return { query, cache, queue, rows, focus, listRef, uploadButtonRef };
}

function RejectedNotice({ rejected, onDismiss }: { readonly rejected: { readonly oversized: readonly string[]; readonly unreadable: readonly string[] }; readonly onDismiss: () => void }) {
  const oversized = oversizedMessage(rejected.oversized);
  const unreadable = namesMessage('不是檔案或讀不到，沒有上傳', rejected.unreadable);
  const isEmpty = oversized === null && unreadable === null;
  const content = oversized !== null && unreadable !== null ? <><div>{oversized}</div><div>{unreadable}</div></> : (oversized ?? unreadable);
  return (
    <Notice tone="warning" dismissLabel="知道了（沒有上傳的檔案）" onDismiss={isEmpty ? undefined : onDismiss}>
      {content}
    </Notice>
  );
}

interface AttachmentBodyProps extends Omit<AttachmentListProps, 'now'> {
  readonly query: ReturnType<typeof useAttachments>;
}

/** 第一次讀的時候骨架、第一次就讀不到時錯誤與重試，其餘是清單（背景重抓失敗時畫面照舊，上面有提示）。 */
function AttachmentBody({ query, ...list }: AttachmentBodyProps) {
  if (query.isPending) return <Skeleton label="正在載入附件…" rows={SKELETON_ROWS} />;
  if (query.data === undefined) {
    return <ErrorState error={query.error} title="附件載入失敗" onRetry={() => void query.refetch()} />;
  }
  return <AttachmentList {...list} now={query.dataUpdatedAt} />;
}

/** 一直掛著的即時區域（先掛空的，之後才填字報讀軟體才唸得到）：上傳完成或失敗的結果。 */
function UploadStatus({ text }: { readonly text: string }) {
  return (
    <p role="status" className="visually-hidden">
      {text}
    </p>
  );
}

interface AttachmentHeaderProps {
  readonly headingId: string;
  /** 0 時不顯示數量（空的狀態自己會說）。 */
  readonly count: number;
  readonly upload: ReactNode;
}

function AttachmentHeader({ headingId, count, upload }: AttachmentHeaderProps) {
  return (
    <div className={styles.header}>
      <div className={styles.titleLine}>
        <SectionHeading id={headingId} className={styles.heading}>
          附件
        </SectionHeading>
        {count > 0 && <CountBadge value={count} />}
      </div>
      {upload}
    </div>
  );
}

/**
 * 項目的附件（docs/design/08-interaction.md 8.3「項目的附件」）：標題旁是數量；每一個附件一列
 * （類型圖示、檔名連結、大小、上傳的人與時間、「刪除」）；有 item.update 而且專案沒封存時有「上傳檔案」，
 * 也可以把檔案拖進整個區塊。每個上傳一列進度，同時最多 3 個，其他排隊。
 * 位置在「說明」後面、「下層項目」前面；項目頁與項目面板共用。
 * 清單還沒讀到（載入中、讀不到）之前沒有「上傳檔案」、也不收拖進來的檔案：佇列的列畫在清單裡，收了使用者看不到進度與失敗，也取消不了。
 */
export function AttachmentSection({ item }: { readonly item: ItemDetail }) {
  const { detail, can } = useProjectTable();
  const canManage = can.update && detail.project.archivedAt === null;
  const headingId = useId();
  const { query, cache, queue, rows, focus, listRef, uploadButtonRef } = useAttachmentModel(item.key);
  const [deleting, setDeleting] = useState<Attachment | null>(null);
  const intake = useFileIntake(queue.add);
  const canUpload = canManage && query.data !== undefined;
  const drop = useFileDrop(canUpload, intake.addFiles);
  const announcement = useUploadAnnouncement(queue.entries);
  return (
    <section className={styles.section} aria-labelledby={headingId} data-dragging={drop.isDragging || undefined} {...drop.handlers}>
      <AttachmentHeader
        headingId={headingId}
        count={query.data?.length ?? 0}
        upload={canUpload ? <UploadButton buttonRef={uploadButtonRef} onFiles={intake.addFiles} /> : null}
      />
      <RejectedNotice rejected={intake.rejected} onDismiss={intake.dismiss} />
      <UploadStatus text={announcement} />
      <RefetchErrorNotice query={query} message="無法更新附件的清單，畫面上可能是舊的。" />
      <AttachmentBody
        query={query}
        rows={rows}
        listRef={listRef}
        canManage={canManage}
        onDelete={setDeleting}
        onCancelUpload={(entryId) => {
          focus.planRemoval(entryId);
          queue.remove(entryId);
        }}
        onRetryUpload={queue.retry}
      />
      {deleting !== null && (
        <DeleteAttachmentDialog
          attachment={deleting}
          onClose={() => {
            setDeleting(null);
          }}
          onDeleted={(removed) => {
            focus.planRemoval(removed.id);
            setDeleting(null);
            queue.forget(removed.id);
            cache.removed(removed.id);
          }}
        />
      )}
    </section>
  );
}
