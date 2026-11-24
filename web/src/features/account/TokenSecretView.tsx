import { useEffect, useId, useRef, useState } from 'react';
import { Button } from '../../components/Button/Button';
import { TextField } from '../../components/Field/Field';
import { DialogActions } from '../../components/Dialog/Dialog';
import { Notice } from '../../components/Notice/Notice';
import { COMMAND_NAME } from '../../config/product';
import styles from './AccessTokens.module.css';

type CopyResult = 'copied' | 'failed' | null;

/** 複製到剪貼簿；瀏覽器不給（不是安全連線、沒有權限）時回 false，由呼叫端說明可以手動選取。 */
export async function copyToClipboard(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

/** 欄位的焦點與選取、複製的結果。 */
function useSecretField(secret: string) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [result, setResult] = useState<CopyResult>(null);

  function selectAll() {
    inputRef.current?.focus();
    inputRef.current?.select();
  }

  // 剛才按下的「建立」按鈕已經換掉了，焦點在這裡接手，不掉回頁面本體。
  useEffect(selectAll, []);

  async function copy() {
    const isCopied = await copyToClipboard(secret);
    setResult(isCopied ? 'copied' : 'failed');
    if (!isCopied) selectAll();
  }

  return { inputRef, result, copy };
}

/**
 * 建立之後的畫面：明文放在唯讀的欄位裡（掛上時選取、焦點在欄位上，手動複製也順手），「複製」按鈕，CLI 的用法。
 * 明文只存在這個元件的 props 裡：對話框關上就卸下，沒有存進快取、草稿或 localStorage。
 */
export function TokenSecretView({ secret, onDone }: { readonly secret: string; readonly onDone: () => void }) {
  const { inputRef, result, copy } = useSecretField(secret);
  const warningId = useId();
  return (
    <div className={styles.secretView}>
      <Notice tone="warning" role="none" id={warningId}>
        這串權杖只會顯示這一次，關上之後就看不到了。請現在複製、存到安全的地方。
      </Notice>
      <TextField
        ref={inputRef}
        label="存取權杖"
        value={secret}
        readOnly
        aria-describedby={warningId}
        autoComplete="off"
        spellCheck={false}
        className={styles.secretField}
        hint={
          <>
            在 CLI 執行 <code>{COMMAND_NAME} auth login --paste</code> 再貼上它；CI 裡改設環境變數 <code>KUMI_TOKEN</code>。
          </>
        }
      />
      <div className={styles.copyRow}>
        <Button onClick={() => { void copy(); }}>複製</Button>
        <Notice tone="success" role="status">
          {result === 'copied' ? '已複製。' : null}
        </Notice>
      </div>
      <Notice tone="warning" role="status">
        {result === 'failed' ? '無法自動複製，請選取上面的權杖手動複製。' : null}
      </Notice>
      <DialogActions>
        <Button variant="primary" onClick={onDone}>
          完成
        </Button>
      </DialogActions>
    </div>
  );
}
