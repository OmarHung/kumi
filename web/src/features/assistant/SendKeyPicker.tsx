import { Select, type SelectOption } from '../../components/Select/Select';
import { sendKeyDescription, sendKeyLabel, sendKeyShort, setSendKey, SEND_KEYS, type SendKey } from './sendKey';
import styles from './Assistant.module.css';

interface SendKeyPickerProps {
  readonly sendKey: SendKey;
  /** 窄的地方（浮動面板）：鈕上只留按鍵，完整的說法只給報讀軟體。 */
  readonly isCompact: boolean;
}

/** 送出鈕左邊的小選單：現在用哪個按法送出；選了立刻生效，記在這個瀏覽器（整頁、面板與其他分頁共用）。 */
export function SendKeyPicker({ sendKey, isCompact }: SendKeyPickerProps) {
  const options: readonly SelectOption[] = SEND_KEYS.map((key) => ({ value: key, label: sendKeyDescription(key) }));
  return (
    <Select
      aria-label="送出的按法"
      size="sm"
      width="content"
      align="end"
      showChevron
      className={isCompact ? styles.sendKeyCompact : undefined}
      value={sendKey}
      options={options}
      display={
        isCompact ? (
          <>
            <span aria-hidden="true">{sendKeyShort(sendKey)}</span>
            <span className="visually-hidden">{sendKeyLabel(sendKey)}</span>
          </>
        ) : (
          <span className={styles.sendKeyLabel}>{sendKeyLabel(sendKey)}</span>
        )
      }
      onChange={(value) => {
        const next = SEND_KEYS.find((key) => key === value);
        if (next !== undefined) setSendKey(next);
      }}
    />
  );
}
