import { useEffect, useRef, useState } from 'react';
import { IconButton } from '../../components/IconButton/IconButton';
import styles from './Assistant.module.css';

type CopyState = 'idle' | 'copied' | 'failed';

/** 打勾與說明停留的時間，之後換回複製的圖示。 */
const FEEDBACK_MS = 2000;

async function writeClipboard(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    // 剪貼簿不能用（非安全的來源、權限被拒）：由呼叫端顯示「無法複製」。
    return false;
  }
}

/**
 * 複製一則訊息的文字。按了圖示換成打勾，旁邊的 role="status" 報讀「已複製」，2 秒後換回來；
 * 剪貼簿不能用時顯示「無法複製」。說明靠右、在按鈕左邊展開，不推動別的東西。
 * 平常看不到（滑鼠移過訊息或鍵盤焦點進來才出現，觸控一律顯示）；說明出現時按鈕一定看得到。
 */
export function CopyMessageButton({ text }: { readonly text: string }) {
  const [state, setState] = useState<CopyState>('idle');
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (timer.current !== null) clearTimeout(timer.current);
    },
    [],
  );
  const copy = () => {
    void writeClipboard(text).then((isCopied) => {
      setState(isCopied ? 'copied' : 'failed');
      if (timer.current !== null) clearTimeout(timer.current);
      timer.current = setTimeout(() => {
        setState('idle');
      }, FEEDBACK_MS);
    });
  };
  return (
    <span className={styles.copyGroup} data-state={state}>
      <span className={styles.copyStatus} role="status">
        {state === 'copied' ? '已複製' : state === 'failed' ? '無法複製' : ''}
      </span>
      <IconButton className={styles.copyButton} icon={state === 'copied' ? 'check' : 'copy'} size="sm" label="複製訊息" onClick={copy} />
    </span>
  );
}
