import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { ATTACHMENT_MAX_BYTES, uploadFile, type UploadOptions } from '../../../api/attachments';
import { pageAttachmentUploadUrl } from '../../../api/knowledge';
import type { KnowledgeAttachment } from '../../../api/knowledgeTypes';
import { queryKeys } from '../../../api/queryKeys';
import { useEditorUploads, type EditorUploads } from '../../attachments/useEditorUploads';

export const NO_PAGE_UPLOAD_PERMISSION = '你不能在這一頁上傳檔案';

interface PageEditorUploadsOptions {
  readonly spaceKey: string;
  readonly pageNumber: number;
  /** 有權限編輯這一頁、而且頁面沒被刪除。 */
  readonly canUpload: boolean;
  readonly value: string;
  readonly onChange: (next: string) => void;
  readonly maxLength: number;
  /** 編輯器唯讀（儲存中）：貼上、拖進來、選檔一律忽略。 */
  readonly isReadOnly?: boolean;
}

/**
 * 知識庫頁面的編輯器上傳：貼上、拖進來、「插入圖片或檔案」都傳到這一頁的附件，
 * 傳完讓這一頁的附件清單重抓。佇列、記號與錯誤說明和項目共用 useEditorUploads。
 */
export function usePageEditorUploads({ spaceKey, pageNumber, canUpload, ...rest }: PageEditorUploadsOptions): EditorUploads {
  const queryClient = useQueryClient();
  const upload = useCallback(
    (file: File, options: UploadOptions) => uploadFile<KnowledgeAttachment>(pageAttachmentUploadUrl(spaceKey, pageNumber), file, options),
    [spaceKey, pageNumber],
  );
  const onUploaded = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgePageAttachments(spaceKey, pageNumber), exact: true });
  }, [queryClient, spaceKey, pageNumber]);
  return useEditorUploads({ ...rest, upload, canUpload, onUploaded, noPermissionMessage: NO_PAGE_UPLOAD_PERMISSION, maxBytes: ATTACHMENT_MAX_BYTES });
}
