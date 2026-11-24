import { useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { listAttachments, type Attachment } from '../../api/attachments';
import { queryKeys } from '../../api/queryKeys';

/** 項目的附件，先上傳的在前。 */
export function useAttachments(itemKey: string) {
  return useQuery({
    queryKey: queryKeys.attachments(itemKey),
    queryFn: ({ signal }) => listAttachments(itemKey, signal),
  });
}

/** 上傳、刪除之後：清單與活動都要重抓（活動多一筆「附加了檔案」「刪除了附件」）。 */
function refresh(queryClient: QueryClient, itemKey: string): void {
  void queryClient.invalidateQueries({ queryKey: queryKeys.attachments(itemKey), exact: true });
  void queryClient.invalidateQueries({ queryKey: queryKeys.activity(itemKey) });
}

interface AttachmentCache {
  /** 傳完一個：先放進清單的快取（馬上看得到），再重抓對時。 */
  readonly added: (attachment: Attachment) => void;
  /** 刪掉一個：先從清單的快取拿掉，再重抓對時。 */
  readonly removed: (attachmentId: string) => void;
}

export function useAttachmentCache(itemKey: string): AttachmentCache {
  const queryClient = useQueryClient();
  const key = queryKeys.attachments(itemKey);
  return {
    added: (attachment) => {
      queryClient.setQueryData<readonly Attachment[]>(key, (list) =>
        list === undefined || list.some((existing) => existing.id === attachment.id) ? list : [...list, attachment],
      );
      refresh(queryClient, itemKey);
    },
    removed: (attachmentId) => {
      queryClient.setQueryData<readonly Attachment[]>(key, (list) => list?.filter((existing) => existing.id !== attachmentId));
      refresh(queryClient, itemKey);
    },
  };
}
