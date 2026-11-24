import type { EditorMode } from '../../lib/editorMode';

/** 剩下的字數少於上限的這個比例時，才顯示「還可以輸入幾個字」；平常不必分心看。 */
const REMAINING_HINT_RATIO = 0.1;

const countFormat = new Intl.NumberFormat('zh-TW');

export interface CountNote {
  readonly text: string;
  readonly isOver: boolean;
}

/** 字數的提示：接近上限時「還可以輸入 N 個字」，超過時「超過上限 N 個字」；離上限還遠就沒有。 */
export function countNoteOf(length: number, maxLength: number): CountNote | null {
  const remaining = maxLength - length;
  if (remaining < 0) return { text: `超過上限 ${countFormat.format(-remaining)} 個字`, isOver: true };
  return remaining <= maxLength * REMAINING_HINT_RATIO ? { text: `還可以輸入 ${countFormat.format(remaining)} 個字`, isOver: false } : null;
}

/** 留言與說明的格式提示（docs/DESIGN.md 7.4）：有成員可以提及時多說怎麼提及；Markdown 原文模式開頭改說在編輯原文。 */
export function formatHintOf(canMention: boolean, canAttach: boolean, mode: EditorMode = 'rich'): string {
  const parts = [mode === 'markdown' ? 'Markdown 原文' : '支援 Markdown', ...(canMention ? ['輸入 @ 提及成員'] : []), ...(canAttach ? ['可以貼上截圖，或把圖片與檔案拖進來'] : [])];
  return parts.join('；');
}

/** 編輯器上 aria-describedby 的 id：提示、格式提示、剩餘字數、錯誤，順序和原本的文字框一樣。 */
export function describedByIds(id: string, hasCount: boolean, hasError: boolean): string {
  return [`${id}-hint`, `${id}-format`, hasCount ? `${id}-remaining` : '', hasError ? `${id}-error` : ''].filter((part) => part !== '').join(' ');
}
