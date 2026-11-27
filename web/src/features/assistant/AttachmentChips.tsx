import { useState, type RefObject } from 'react';
import type { AssistantScope } from '../../api/assistant';
import { assistantAttachmentUrl } from '../../api/assistantAttachments';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { attachmentIcon, formatAttachmentSize, isInlineImage } from '../../domain/attachments';
import { isUnreadable, shortenFileName, unreadableShortNote, type EngineSource } from './attachmentRules';
import type { AttachmentEntry } from './useComposerAttachments';
import styles from './Assistant.module.css';

/** 小卡的 × 做記號：移除之後焦點要找得到下一張、前一張的 ×。 */
const REMOVE_ATTRIBUTE = 'data-chip-remove';

function nameOf(entry: AttachmentEntry): string {
  return entry.attachment?.fileName ?? entry.file.name;
}

/**
 * 縮圖：上傳完成的圖片用伺服器的下載網址（內容安全政策只允許同網域與 data:，不能用 blob: 的本機預覽）；
 * 上傳中、失敗、被擋下的，或圖片載入失敗時，顯示檔案圖示。
 */
function Thumbnail({ entry, projectKey }: { readonly entry: AttachmentEntry; readonly projectKey: AssistantScope }) {
  const [hasFailed, setHasFailed] = useState(false);
  const { attachment } = entry;
  const canShowImage = entry.status === 'ready' && attachment !== null && attachment.kind === 'image' && isInlineImage(attachment.contentType) && !hasFailed;
  return (
    <span className={styles.chipThumb} aria-hidden="true">
      {canShowImage ? (
        <img
          className={styles.chipImage}
          src={assistantAttachmentUrl(projectKey, attachment.id)}
          alt=""
          onError={() => {
            setHasFailed(true);
          }}
        />
      ) : (
        <Icon name={attachmentIcon(attachment?.contentType ?? entry.file.type, entry.file.name)} size={20} />
      )}
    </span>
  );
}

/** 小卡第二行：大小，或上傳中、失敗的簡短狀態（完整的原因在小卡列下面一行一行列出，觸控與鍵盤也看得到）。 */
function ChipStatus({ entry, unreadableNote }: { readonly entry: AttachmentEntry; readonly unreadableNote: string | null }) {
  if (entry.status === 'uploading') {
    return (
      <span className={styles.chipMeta}>
        <span className={styles.spinner} aria-hidden="true" />
        上傳中…
      </span>
    );
  }
  if (entry.status === 'failed' || entry.status === 'rejected') {
    return (
      <span className={styles.chipMeta} data-tone="danger">
        {entry.status === 'failed' ? '上傳失敗' : '沒有上傳'}
      </span>
    );
  }
  if (unreadableNote !== null) {
    return (
      <span className={styles.chipMeta} data-tone="danger" title={unreadableNote}>
        {unreadableNote}
      </span>
    );
  }
  return <span className={styles.chipMeta}>{formatAttachmentSize(entry.attachment?.sizeBytes ?? entry.file.size)}</span>;
}

interface ChipProps {
  readonly entry: AttachmentEntry;
  readonly projectKey: AssistantScope;
  /** 選的來源是本機或主機的引擎時是它（Codex 讀不到 PDF）；API 的模型是 null。 */
  readonly source: EngineSource | null;
  /** 送出中：訊息的請求正帶著這些附件，不能移除也不能重試。 */
  readonly isLocked: boolean;
  readonly onRemove: (event: React.MouseEvent<HTMLElement>, id: string) => void;
  readonly onRetry: (event: React.MouseEvent<HTMLElement>, id: string) => void;
}

function Chip({ entry, projectKey, source, isLocked, onRemove, onRetry }: ChipProps) {
  const unreadableNote = source !== null && entry.status === 'ready' && isUnreadable(source, entry.kind) ? unreadableShortNote(source) : null;
  const isUnsupported = unreadableNote !== null;
  const name = nameOf(entry);
  const remove = { [REMOVE_ATTRIBUTE]: '' };
  return (
    <li className={styles.chip} data-status={entry.status} data-unsupported={isUnsupported ? 'true' : undefined}>
      <Thumbnail entry={entry} projectKey={projectKey} />
      <span className={styles.chipText}>
        <span className={styles.chipName} title={name}>
          {shortenFileName(name)}
        </span>
        <ChipStatus entry={entry} unreadableNote={unreadableNote} />
      </span>
      {entry.status === 'failed' && entry.canRetry && (
        <Button
          size="sm"
          aria-label={`重試上傳 ${name}`}
          disabled={isLocked}
          onClick={(event) => {
            onRetry(event, entry.id);
          }}
        >
          重試
        </Button>
      )}
      <IconButton
        {...remove}
        icon="close"
        size="sm"
        label={`移除附件 ${name}`}
        disabled={isLocked}
        onClick={(event) => {
          onRemove(event, entry.id);
        }}
      />
    </li>
  );
}

/** 失敗與沒有上傳的完整原因：每張一行、可以換行（小卡的高度固定，放不下整句）。在小卡列下面、文字框上面，文字框的位置不動。 */
function Problems({ entries }: { readonly entries: readonly AttachmentEntry[] }) {
  const problems = entries.filter((entry) => (entry.status === 'failed' || entry.status === 'rejected') && entry.message !== null);
  if (problems.length === 0) return null;
  return (
    <ul className={styles.attachProblems} aria-label="附件的問題">
      {problems.map((entry) => (
        <li key={entry.id}>{`${nameOf(entry)}：${entry.message ?? ''}`}</li>
      ))}
    </ul>
  );
}

/** 按下去的那張小卡的 ×，和同一列的所有 ×（依畫面順序）。 */
function removeButtonsAround(event: React.MouseEvent<HTMLElement>) {
  const list = event.currentTarget.closest('ul');
  const buttons = list === null ? [] : Array.from(list.querySelectorAll<HTMLElement>(`[${REMOVE_ATTRIBUTE}]`));
  const own = event.currentTarget.closest('li')?.querySelector<HTMLElement>(`[${REMOVE_ATTRIBUTE}]`) ?? null;
  const index = own === null ? -1 : buttons.indexOf(own);
  return { buttons, index, own };
}

interface AttachmentChipsProps {
  readonly projectKey: AssistantScope;
  readonly entries: readonly AttachmentEntry[];
  readonly source: EngineSource | null;
  readonly isLocked: boolean;
  readonly inputRef: RefObject<HTMLTextAreaElement | null>;
  readonly onRemove: (id: string) => void;
  readonly onRetry: (id: string) => void;
}

/**
 * 文字框上方的一列附件小卡（高度固定，出現與消失只推文字框上方的這一區）與失敗的完整原因。
 * 焦點：移除之後到下一張小卡的 ×，沒有就前一張，再沒有就文字框；重試之後留在那張小卡的 ×（原本的「重試」會消失）。
 */
export function AttachmentChips({ projectKey, entries, source, isLocked, inputRef, onRemove, onRetry }: AttachmentChipsProps) {
  if (entries.length === 0) return null;
  const handleRemove = (event: React.MouseEvent<HTMLElement>, id: string) => {
    const { buttons, index } = removeButtonsAround(event);
    const neighbour = buttons[index + 1] ?? buttons[index - 1] ?? null;
    // 小卡用編號當 key：鄰居的元素在移除之後還是同一個，現在就可以把焦點給它。
    (neighbour ?? inputRef.current)?.focus();
    onRemove(id);
  };
  const handleRetry = (event: React.MouseEvent<HTMLElement>, id: string) => {
    removeButtonsAround(event).own?.focus();
    onRetry(id);
  };
  return (
    <>
      <ul className={styles.chips} aria-label="附件">
        {entries.map((entry) => (
          <Chip key={entry.id} entry={entry} projectKey={projectKey} source={source} isLocked={isLocked} onRemove={handleRemove} onRetry={handleRetry} />
        ))}
      </ul>
      <Problems entries={entries} />
    </>
  );
}
