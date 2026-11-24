import type { Attachment } from '../../api/attachments';
import { attachmentContentUrl } from '../../api/attachments';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { ListRow, type ListMeta } from '../../components/List/List';
import { attachmentIcon, formatAttachmentSize, isInlineImage } from '../../domain/attachments';
import { formatInstant, formatRelativeTime } from '../../domain/time';
import { ROW_ACTION_ATTRIBUTE } from './useRemovalFocus';
import type { UploadEntry } from './useUploadQueue';
import styles from './AttachmentSection.module.css';

function FileIcon({ contentType, fileName }: { readonly contentType: string; readonly fileName: string }) {
  return (
    <span className={styles.fileIcon}>
      <Icon name={attachmentIcon(contentType, fileName)} size={20} />
    </span>
  );
}

/**
 * 檔名連結：圖片（png、jpeg、gif、webp）在新分頁直接開，其他下載（伺服器也是這樣回的，
 * 這裡的 download 只是讓瀏覽器不要換頁）。名稱太長用省略號，完整的名稱在 title。
 */
function FileLink({ attachment }: { readonly attachment: Attachment }) {
  const href = attachmentContentUrl(attachment.id);
  if (isInlineImage(attachment.contentType)) {
    return (
      <a className={styles.link} href={href} target="_blank" rel="noopener" title={attachment.fileName}>
        {attachment.fileName}
      </a>
    );
  }
  return (
    <a className={styles.link} href={href} download={attachment.fileName} rel="noopener" title={attachment.fileName}>
      {attachment.fileName}
    </a>
  );
}

interface AttachmentRowProps {
  readonly attachment: Attachment;
  /** 算相對時間用的「現在」：整份清單用同一個值。 */
  readonly now: number;
  /** 沒有 item.update、或專案封存了：沒有刪除。 */
  readonly onDelete: ((attachment: Attachment) => void) | null;
}

/** 一個附件：圖示、檔名連結、大小、上傳的人與時間，最右邊的「刪除」。 */
export function AttachmentRow({ attachment, now, onDelete }: AttachmentRowProps) {
  const meta: readonly ListMeta[] = [
    { key: 'size', label: '大小', content: formatAttachmentSize(attachment.sizeBytes) },
    { key: 'by', label: '上傳者', content: attachment.uploadedBy.displayName },
    {
      key: 'at',
      label: '上傳時間',
      content: (
        <time dateTime={attachment.uploadedAt} title={formatInstant(attachment.uploadedAt)}>
          {formatRelativeTime(attachment.uploadedAt, now)}
        </time>
      ),
    },
  ];
  return (
    <ListRow
      leading={<FileIcon contentType={attachment.contentType} fileName={attachment.fileName} />}
      title={<FileLink attachment={attachment} />}
      meta={meta}
      trailing={
        onDelete === null ? undefined : (
          <Button
            variant="ghost"
            size="sm"
            aria-label={`刪除 ${attachment.fileName}`}
            {...{ [ROW_ACTION_ATTRIBUTE]: attachment.id }}
            onClick={() => {
              onDelete(attachment);
            }}
          >
            刪除
          </Button>
        )
      }
    />
  );
}

function percentOf(entry: UploadEntry): number | null {
  if (entry.total === null || entry.total <= 0) return null;
  return Math.min(100, Math.floor((entry.sent / entry.total) * 100));
}

function UploadProgress({ entry }: { readonly entry: UploadEntry }) {
  const percent = percentOf(entry);
  return (
    <span className={styles.progressLine}>
      <progress
        className={styles.progress}
        max={100}
        value={percent ?? undefined}
        aria-label={`${entry.file.name} 的上傳進度`}
      />
      <span className={styles.percent}>{percent === null ? '上傳中…' : `${String(percent)}%`}</span>
    </span>
  );
}

function uploadMeta(entry: UploadEntry): readonly ListMeta[] {
  if (entry.status === 'queued') {
    return [{ key: 'state', content: '等待上傳…' }];
  }
  if (entry.status === 'failed') {
    return [{ key: 'state', content: <span className={styles.failure}>{`上傳失敗：${entry.message ?? '請再試一次。'}`}</span> }];
  }
  return [{ key: 'state', content: <UploadProgress entry={entry} /> }];
}

interface UploadRowProps {
  readonly entry: UploadEntry;
  readonly onCancel: (entryId: string) => void;
  readonly onRetry: (entryId: string) => void;
}

/**
 * 上傳中的一列：檔名、進度（百分比加進度條）與「取消」；排隊中寫「等待上傳…」；失敗時寫原因，有「重試」與「移除」。
 * 高度與正式的一列一樣（同樣的 ListRow 兩行），傳完換成正式的列時不跳動。
 */
export function UploadRow({ entry, onCancel, onRetry }: UploadRowProps) {
  const name = entry.file.name;
  const isFailed = entry.status === 'failed';
  return (
    <ListRow
      leading={<FileIcon contentType={entry.file.type} fileName={name} />}
      title={<span title={name}>{name}</span>}
      meta={uploadMeta(entry)}
      trailing={
        <>
          {isFailed && (
            <Button
              variant="ghost"
              size="sm"
              aria-label={`重試 ${name}`}
              onClick={() => {
                onRetry(entry.id);
              }}
            >
              重試
            </Button>
          )}
          <Button
            variant="ghost"
            size="sm"
            aria-label={`${isFailed ? '移除' : '取消'} ${name}`}
            {...{ [ROW_ACTION_ATTRIBUTE]: entry.id }}
            onClick={() => {
              onCancel(entry.id);
            }}
          >
            {isFailed ? '移除' : '取消'}
          </Button>
        </>
      }
    />
  );
}
