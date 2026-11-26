import { useState } from 'react';
import { UNSUPPORTED_SOURCE_NOTE } from './attachmentRules';
import type { AttachmentEntry } from './useComposerAttachments';

function nameOf(entry: AttachmentEntry): string {
  return entry.attachment?.fileName ?? entry.file.name;
}

/** 這一輪狀態變了的小卡該報什麼：上傳完成、上傳失敗、沒有上傳。 */
function changes(entries: readonly AttachmentEntry[], seen: ReadonlyMap<string, string>): readonly string[] {
  return entries.flatMap((entry) => {
    if (seen.get(entry.id) === entry.status) return [];
    if (entry.status === 'ready') return [`已上傳 ${nameOf(entry)}`];
    if (entry.status === 'failed') return [`${nameOf(entry)} 上傳失敗：${entry.message ?? ''}`];
    if (entry.status === 'rejected') return [`${nameOf(entry)} 沒有上傳：${entry.message ?? ''}`];
    return [];
  });
}

interface Seen {
  readonly statuses: ReadonlyMap<string, string>;
  readonly wasUnsupported: boolean;
  readonly message: string;
}

/**
 * 附件狀態變化的報讀文字（給一個共用的 role="status"）：上傳完成、失敗，以及「這個來源讀不到圖片與 PDF」剛出現時。
 * 在渲染時比較上一輪（不必 effect 再跑一輪）；同時有好幾張就一起唸。
 */
export function useAttachmentAnnouncement(entries: readonly AttachmentEntry[], hasUnsupported: boolean): string {
  const [seen, setSeen] = useState<Seen>({ statuses: new Map(), wasUnsupported: false, message: '' });
  const isSame = entries.length === seen.statuses.size && entries.every((entry) => seen.statuses.get(entry.id) === entry.status);
  if (!isSame || hasUnsupported !== seen.wasUnsupported) {
    const lines = [...changes(entries, seen.statuses), ...(hasUnsupported && !seen.wasUnsupported ? [UNSUPPORTED_SOURCE_NOTE] : [])];
    setSeen({
      statuses: new Map(entries.map((entry) => [entry.id, entry.status])),
      wasUnsupported: hasUnsupported,
      message: lines.length > 0 ? lines.join('。') : seen.message,
    });
  }
  return seen.message;
}
