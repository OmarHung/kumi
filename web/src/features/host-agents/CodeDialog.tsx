import type { ReactNode } from 'react';
import { Button } from '../../components/Button/Button';
import { Dialog, DialogActions } from '../../components/Dialog/Dialog';
import { formatInstant } from '../../domain/time';
import { CopyBlock } from '../account/ConnectAgentSection';
import styles from './HostAgents.module.css';

interface CodeDialogProps {
  readonly title: string;
  readonly description: ReactNode;
  /** 一次性的碼（配對碼、登入碼）：只在這個對話框裡，關上就沒了。 */
  readonly code: string;
  readonly codeLabel: string;
  readonly expiresAt: string;
  /** 要在主機上執行的指令。 */
  readonly command: string;
  readonly commandLabel: string;
  /** 指令下面補充的說明（登入的限制）。 */
  readonly extra?: ReactNode;
  readonly onClose: () => void;
}

/**
 * 顯示一次性的碼加上要在主機上執行的指令，兩個都可以複製。碼只放在呼叫端的 state、不進查詢的快取、不寫進瀏覽器的儲存：
 * 關上之後要再拿，就重新配對（或重新開始登入）。
 */
export function CodeDialog({ title, description, code, codeLabel, expiresAt, command, commandLabel, extra, onClose }: CodeDialogProps) {
  return (
    <Dialog title={title} description={description} size="lg" onClose={onClose}>
      <div className={styles.dialogForm}>
        <div className={styles.usageGroup}>
          <CopyBlock label={codeLabel} text={code} />
          <p className={styles.note}>{`${formatInstant(expiresAt)} 前有效，只用得一次。關上這個視窗就看不到了。`}</p>
        </div>
        <div className={styles.usageGroup}>
          <p>在主機上執行這個指令，照提示輸入上面的碼（碼從終端機讀，不用寫在指令後面）：</p>
          <CopyBlock label={commandLabel} text={command} wrap />
        </div>
        {extra}
        <DialogActions>
          <Button variant="primary" onClick={onClose}>
            完成
          </Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}
