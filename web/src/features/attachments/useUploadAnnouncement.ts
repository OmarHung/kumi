import { useState } from 'react';
import type { UploadEntry, UploadStatus } from './useUploadQueue';

function sentenceOf(entry: UploadEntry): string {
  const name = entry.file.name;
  return entry.status === 'done' ? `上傳完成：${name}` : `上傳失敗：${name}，${entry.message ?? '請再試一次。'}`;
}

function isSame(seen: ReadonlyMap<string, UploadStatus>, entries: readonly UploadEntry[]): boolean {
  return seen.size === entries.length && entries.every((entry) => seen.get(entry.id) === entry.status);
}

/**
 * 給螢幕報讀器的一句話：佇列裡的檔案轉成完成或失敗時說出來（失敗連同原因）。
 * 失敗只是列裡多一段字、完成只是換列，不說的話看不到畫面的人不知道結果。
 * 好幾個同時結束時合成一句，用句號接起來。
 */
export function useUploadAnnouncement(entries: readonly UploadEntry[]): string {
  const [seen, setSeen] = useState<ReadonlyMap<string, UploadStatus>>(new Map());
  const [message, setMessage] = useState('');
  if (!isSame(seen, entries)) {
    const finished = entries.filter((entry) => (entry.status === 'done' || entry.status === 'failed') && seen.get(entry.id) !== entry.status);
    setSeen(new Map(entries.map((entry) => [entry.id, entry.status])));
    if (finished.length > 0) setMessage(finished.map(sentenceOf).join('。'));
  }
  return message;
}
