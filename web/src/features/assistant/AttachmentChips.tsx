import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { attachmentIcon, formatAttachmentSize } from '../../domain/attachments';
import { isBinaryKind, shortenFileName, UNSUPPORTED_SOURCE_NOTE } from './attachmentRules';
import type { AttachmentEntry } from './useComposerAttachments';
import styles from './Assistant.module.css';

interface ChipProps {
  readonly entry: AttachmentEntry;
  /** 選的來源讀不到圖片與 PDF。 */
  readonly isLocalSource: boolean;
  readonly onRemove: (id: string) => void;
  readonly onRetry: (id: string) => void;
}

function Thumbnail({ entry }: { readonly entry: AttachmentEntry }) {
  return (
    <span className={styles.chipThumb} aria-hidden="true">
      {entry.previewUrl === null ? (
        <Icon name={attachmentIcon(entry.attachment?.contentType ?? entry.file.type, entry.file.name)} size={20} />
      ) : (
        <img className={styles.chipImage} src={entry.previewUrl} alt="" />
      )}
    </span>
  );
}

/** 小卡第二行：大小，或上傳中、失敗的原因（太長用省略號，完整的在 title）。 */
function ChipStatus({ entry, isUnsupported }: { readonly entry: AttachmentEntry; readonly isUnsupported: boolean }) {
  if (entry.status === 'uploading') {
    return (
      <span className={styles.chipMeta} role="status">
        <span className={styles.spinner} aria-hidden="true" />
        上傳中…
      </span>
    );
  }
  if (entry.status === 'failed' || entry.status === 'rejected') {
    return (
      <span className={styles.chipMeta} data-tone="danger" role="alert" title={entry.message ?? undefined}>
        {entry.message}
      </span>
    );
  }
  if (isUnsupported) {
    return (
      <span className={styles.chipMeta} data-tone="danger" title={UNSUPPORTED_SOURCE_NOTE}>
        {UNSUPPORTED_SOURCE_NOTE}
      </span>
    );
  }
  return <span className={styles.chipMeta}>{formatAttachmentSize(entry.attachment?.sizeBytes ?? entry.file.size)}</span>;
}

function Chip({ entry, isLocalSource, onRemove, onRetry }: ChipProps) {
  const isUnsupported = isLocalSource && entry.status === 'ready' && isBinaryKind(entry.kind);
  const name = entry.attachment?.fileName ?? entry.file.name;
  return (
    <li className={styles.chip} data-status={entry.status} data-unsupported={isUnsupported ? 'true' : undefined}>
      <Thumbnail entry={entry} />
      <span className={styles.chipText}>
        <span className={styles.chipName} title={name}>
          {shortenFileName(name)}
        </span>
        <ChipStatus entry={entry} isUnsupported={isUnsupported} />
      </span>
      {entry.status === 'failed' && (
        <Button
          size="sm"
          aria-label={`重試上傳 ${name}`}
          onClick={() => {
            onRetry(entry.id);
          }}
        >
          重試
        </Button>
      )}
      <IconButton
        icon="close"
        size="sm"
        label={`移除附件 ${name}`}
        onClick={() => {
          onRemove(entry.id);
        }}
      />
    </li>
  );
}

interface AttachmentChipsProps {
  readonly entries: readonly AttachmentEntry[];
  readonly isLocalSource: boolean;
  readonly onRemove: (id: string) => void;
  readonly onRetry: (id: string) => void;
}

/** 文字框上方的一列附件小卡：高度固定，出現與消失只推文字框上方的這一區。 */
export function AttachmentChips({ entries, isLocalSource, onRemove, onRetry }: AttachmentChipsProps) {
  if (entries.length === 0) return null;
  return (
    <ul className={styles.chips} aria-label="附件">
      {entries.map((entry) => (
        <Chip key={entry.id} entry={entry} isLocalSource={isLocalSource} onRemove={onRemove} onRetry={onRetry} />
      ))}
    </ul>
  );
}
