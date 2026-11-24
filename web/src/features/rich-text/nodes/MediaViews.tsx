import { NodeViewWrapper, type NodeViewProps } from '@tiptap/react';
import { useContext, useState } from 'react';
import { attachmentContentUrl } from '../../../api/attachments';
import { Icon, type IconName } from '../../../components/Icon/Icon';
import { attachmentIdOf } from '../../../components/Markdown/markdownUrls';
import markdownStyles from '../../../components/Markdown/Markdown.module.css';
import { attachmentIcon } from '../../../domain/attachments';
import { cx } from '../../../lib/cx';
import { EditorEnvironmentContext } from '../editorContext';
import styles from './nodes.module.css';

interface CardProps {
  readonly icon: IconName;
  readonly text: string;
  readonly state?: 'uploading';
}

function Card({ icon, text, state }: CardProps) {
  return (
    <span className={styles.card} data-state={state}>
      <Icon name={icon} className={styles.cardIcon} />
      <span className={styles.cardName}>{text}</span>
    </span>
  );
}

/**
 * 自己項目的附件圖片：和顯示時一樣的同網域下載端點、同樣的最大高度；載入失敗換成一行字，名稱沿用寫在文字裡的檔名。
 * 編輯器裡沒有連結：點圖片是選取它（可以刪掉），不開新分頁。
 */
function AttachmentImage({ attachmentId, name }: { readonly attachmentId: string; readonly name: string }) {
  const [hasFailed, setHasFailed] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  if (hasFailed) return <span className={markdownStyles.imageGone}>圖片已刪除或無法顯示：{name}</span>;
  return (
    <span className={styles.media}>
      <img
        src={attachmentContentUrl(attachmentId)}
        alt={name}
        loading="lazy"
        draggable={false}
        className={markdownStyles.image}
        data-loading={isLoading || undefined}
        onLoad={() => {
          setIsLoading(false);
        }}
        onError={() => {
          setHasFailed(true);
        }}
      />
    </span>
  );
}

interface ImageAttributes {
  readonly src: string;
  readonly alt: string | null;
}

/** 圖片：自己的附件（而且這段文字有附件可以對）顯示圖片；其他網址不載入（內容安全政策只允許同網域），畫成「圖片：名稱」的小卡。 */
export function ImageView({ node }: NodeViewProps) {
  const { hasAttachments } = useContext(EditorEnvironmentContext);
  const { src, alt } = node.attrs as ImageAttributes;
  const attachmentId = attachmentIdOf(src);
  // 沒寫說明文字的圖片（![](…)）：名稱退回網址，再退回「圖片」。
  const name = (alt ?? '').trim() === '' ? '圖片' : (alt ?? '');
  return (
    <NodeViewWrapper as="span" className={styles.media}>
      {attachmentId !== null && hasAttachments ? <AttachmentImage key={attachmentId} attachmentId={attachmentId} name={name} /> : <Card icon="fileImage" text={`圖片：${name}`} />}
    </NodeViewWrapper>
  );
}

interface FileAttributes {
  readonly name: string;
}

/** 檔案：圖示（和附件清單同一套）加檔名的小卡。 */
export function AttachmentFileView({ node }: NodeViewProps) {
  const { name } = node.attrs as FileAttributes;
  return (
    <NodeViewWrapper as="span" className={cx(styles.media)}>
      <Card icon={attachmentIcon('', name)} text={name} />
    </NodeViewWrapper>
  );
}

interface PlaceholderAttributes {
  readonly name: string;
}

/** 上傳中：「上傳中：檔名…」的小卡，傳完由上傳的目標換成圖片或檔案。 */
export function UploadPlaceholderView({ node }: NodeViewProps) {
  const { name } = node.attrs as PlaceholderAttributes;
  return (
    <NodeViewWrapper as="span" className={styles.media}>
      <Card icon="upload" text={`上傳中：${name}…`} state="uploading" />
    </NodeViewWrapper>
  );
}
