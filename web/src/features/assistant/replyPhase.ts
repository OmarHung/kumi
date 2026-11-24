/**
 * 主機來源才有的進度：queued 是還在排隊（position 是前面還有幾件再加一，1 是下一個），started 是輪到了、開始執行。
 * 內建與自己的連線沒有這兩個事件，一直是 null。
 */
export type ReplyPhase = { readonly type: 'queued'; readonly position: number } | { readonly type: 'started' } | null;

/** 畫面上與報讀軟體說的一句：排隊中前面還有幾件（position − 1）、執行中；沒有進度的是原本的「回覆中」。 */
export function replyPhaseText(phase: ReplyPhase): string {
  if (phase === null) return '回覆中…';
  if (phase.type === 'started') return '執行中…';
  const ahead = phase.position - 1;
  return ahead <= 0 ? '排隊中，下一個就輪到你' : `排隊中，前面還有 ${String(ahead)} 件`;
}
