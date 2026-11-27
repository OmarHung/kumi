import type { AssistantAttachment } from '../../api/aiTypes';
import type { AssistantScope } from '../../api/assistant';
import { assistantAttachmentUrl } from '../../api/assistantAttachments';
import { Icon } from '../../components/Icon/Icon';
import { attachmentIcon, formatAttachmentSize, isInlineImage } from '../../domain/attachments';
import { shortenFileName } from './attachmentRules';
import styles from './Assistant.module.css';

interface AttachmentProps {
  readonly attachment: AssistantAttachment;
  /** null 是不選專案的組織對話（附件的網址不同）。 */
  readonly projectKey: AssistantScope;
}

/** 圖片：固定大小的縮圖，點了在新分頁打開原圖（伺服器只讓四種點陣圖直接顯示）。 */
function ImageAttachment({ attachment, projectKey }: AttachmentProps) {
  const href = assistantAttachmentUrl(projectKey, attachment.id);
  return (
    <a className={styles.attachedImage} href={href} target="_blank" rel="noopener" title={attachment.fileName}>
      <img src={href} alt={attachment.fileName} loading="lazy" />
    </a>
  );
}

/** 其他檔案：圖示、檔名、大小的小卡，點了下載。 */
function FileAttachment({ attachment, projectKey }: AttachmentProps) {
  return (
    <a className={styles.attachedFile} href={assistantAttachmentUrl(projectKey, attachment.id)} download={attachment.fileName} rel="noopener" title={attachment.fileName}>
      <Icon name={attachmentIcon(attachment.contentType, attachment.fileName)} size={20} />
      <span className={styles.attachedFileText}>
        <span className={styles.chipName}>{shortenFileName(attachment.fileName)}</span>
        <span className={styles.chipMeta}>{formatAttachmentSize(attachment.sizeBytes)}</span>
      </span>
    </a>
  );
}

/** 使用者訊息帶的附件：圖片縮圖與檔案小卡排成一列（太多時換行）。 */
export function MessageAttachments({ attachments, projectKey }: { readonly attachments: readonly AssistantAttachment[]; readonly projectKey: AssistantScope }) {
  if (attachments.length === 0) return null;
  return (
    <ul className={styles.attachedList} aria-label="附件">
      {attachments.map((attachment) => (
        <li key={attachment.id}>
          {attachment.kind === 'image' && isInlineImage(attachment.contentType) ? (
            <ImageAttachment attachment={attachment} projectKey={projectKey} />
          ) : (
            <FileAttachment attachment={attachment} projectKey={projectKey} />
          )}
        </li>
      ))}
    </ul>
  );
}
