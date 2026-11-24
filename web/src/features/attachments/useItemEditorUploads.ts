import { useCallback } from 'react';
import { ATTACHMENT_MAX_BYTES, uploadAttachment } from '../../api/attachments';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { useAttachmentCache } from './useAttachments';
import { useEditorUploads, type EditorUploads, type EditorUploadsOptions } from './useEditorUploads';

export const NO_UPLOAD_PERMISSION = '你不能在這個項目上傳檔案';

type ItemEditorUploadsOptions = Pick<EditorUploadsOptions, 'value' | 'onChange' | 'maxLength' | 'isReadOnly'> & {
  readonly itemKey: string;
};

/**
 * 項目的說明與留言的編輯器上傳：上傳到項目的附件，權限是 item.update 而且專案沒封存，
 * 傳完把附件放進項目附件清單的快取（清單與活動重抓）。其餘都是通用的 useEditorUploads。
 */
export function useItemEditorUploads({ itemKey, ...rest }: ItemEditorUploadsOptions): EditorUploads {
  const { detail, can } = useProjectTable();
  const cache = useAttachmentCache(itemKey);
  const upload = useCallback((file: File, options: Parameters<typeof uploadAttachment>[2]) => uploadAttachment(itemKey, file, options), [itemKey]);
  return useEditorUploads({
    ...rest,
    upload,
    canUpload: can.update && detail.project.archivedAt === null,
    onUploaded: cache.added,
    noPermissionMessage: NO_UPLOAD_PERMISSION,
    maxBytes: ATTACHMENT_MAX_BYTES,
  });
}
