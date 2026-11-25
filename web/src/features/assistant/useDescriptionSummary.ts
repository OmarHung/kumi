import { useEffect, useMemo, useState } from 'react';
import type { Member } from '../../api/types';
import type { descriptionAdditions } from './descriptionAdditions';

type Summarize = typeof descriptionAdditions;

export const SUMMARY_FAILED = '沒辦法列出這份說明裡的連結與提及，請自己看過說明再決定要不要套用。';

/**
 * 建議的說明裡的連結與提及（一行字，沒有就是 null）。解析 Markdown 的那一包（remark 全家）有一百多 kB，
 * 這個元件在每一頁的 AI 助理面板裡：真的有說明的建議出現才下載，主程式檔不變大。下載好之前是 null；下載失敗（重新部署之後舊分頁）
 * 回 SUMMARY_FAILED，不讓使用者以為沒有連結。
 */
export function useDescriptionSummary(before: string | undefined, text: string | null, members: readonly Member[] | undefined): string | null {
  const [summarize, setSummarize] = useState<Summarize | 'failed' | null>(null);
  const hasText = text !== null;
  useEffect(() => {
    if (!hasText) return undefined;
    let isCurrent = true;
    import('./descriptionAdditions').then(
      (module) => {
        if (isCurrent) setSummarize(() => module.descriptionAdditions);
      },
      () => {
        if (isCurrent) setSummarize('failed');
      },
    );
    return () => {
      isCurrent = false;
    };
  }, [hasText]);
  return useMemo(() => {
    if (summarize === 'failed') return SUMMARY_FAILED;
    if (summarize === null || text === null) return null;
    return summarize(before, text, members);
  }, [summarize, before, text, members]);
}
