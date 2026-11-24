import type { RefObject } from 'react';
import type { Attachment } from '../../api/attachments';
import { List } from '../../components/List/List';
import { AttachmentRow, UploadRow } from './AttachmentRows';
import type { UploadEntry } from './useUploadQueue';
import styles from './AttachmentSection.module.css';

export type AttachmentRowData =
  | { readonly kind: 'attachment'; readonly id: string; readonly attachment: Attachment }
  | { readonly kind: 'upload'; readonly id: string; readonly entry: UploadEntry };

/**
 * 伺服器上的附件，接著是這次上傳的：傳完但清單還沒重抓到的，用伺服器回的附件畫成正式的一列，
 * 所以上傳的那一列換成正式的列時不會空一下（清單重抓到之後，useUploadQueue 的 settle 把它拿掉）。
 */
export function rowsOf(attachments: readonly Attachment[], entries: readonly UploadEntry[]): readonly AttachmentRowData[] {
  const known = new Set(attachments.map((attachment) => attachment.id));
  const settled = attachments.map<AttachmentRowData>((attachment) => ({ kind: 'attachment', id: attachment.id, attachment }));
  const uploaded = entries.flatMap<AttachmentRowData>((entry) => {
    const { attachment } = entry;
    if (attachment === null) return [{ kind: 'upload', id: entry.id, entry }];
    return known.has(attachment.id) ? [] : [{ kind: 'attachment', id: attachment.id, attachment }];
  });
  return [...settled, ...uploaded];
}

export interface AttachmentListProps {
  readonly rows: readonly AttachmentRowData[];
  readonly listRef: RefObject<HTMLDivElement | null>;
  readonly now: number;
  readonly canManage: boolean;
  readonly onDelete: (attachment: Attachment) => void;
  readonly onCancelUpload: (entryId: string) => void;
  readonly onRetryUpload: (entryId: string) => void;
}

/** 一個附件（或一個上傳）一列；沒有任何一列時說「還沒有附件」，能上傳的人多一句怎麼上傳。 */
export function AttachmentList({ rows, listRef, now, canManage, onDelete, onCancelUpload, onRetryUpload }: AttachmentListProps) {
  if (rows.length === 0) {
    return <p className={styles.empty}>{canManage ? '還沒有附件。把檔案拖進來，或按「上傳檔案」。' : '還沒有附件。'}</p>;
  }
  return (
    <div ref={listRef}>
      <List label="附件">
        {rows.map((row) =>
          row.kind === 'attachment' ? (
            <AttachmentRow key={row.id} attachment={row.attachment} now={now} onDelete={canManage ? onDelete : null} />
          ) : (
            <UploadRow key={row.id} entry={row.entry} onCancel={onCancelUpload} onRetry={onRetryUpload} />
          ),
        )}
      </List>
    </div>
  );
}
