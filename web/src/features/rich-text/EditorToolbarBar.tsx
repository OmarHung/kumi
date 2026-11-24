import { useId, type KeyboardEvent, type ReactNode } from 'react';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import type { EditorMode } from '../../lib/editorMode';
import styles from './EditorToolbar.module.css';

/** 工具列最右邊的「編輯方式」切換的狀態：呼叫端（外殼）決定偏好怎麼存、什麼時候不能切。 */
export interface EditorModeControl {
  readonly mode: EditorMode;
  readonly onChange: (mode: EditorMode) => void;
  /** 暫時不能切換的原因（上傳中、送出中）；null 是可以切。 */
  readonly disabledReason: string | null;
}

const MODE_OPTIONS: readonly { readonly value: EditorMode; readonly label: string }[] = [
  { value: 'rich', label: '所見即所得' },
  { value: 'markdown', label: 'Markdown' },
];

interface EditorToolbarBarProps {
  readonly control: EditorModeControl;
  /** 焦點在切換上時的 Esc 與 Ctrl／⌘+Enter（和在工具列、內容裡一樣；不接住的話 Esc 會落到全域把面板關了）。 */
  readonly onShortcutKey: (event: KeyboardEvent<HTMLElement>) => void;
  /** 所見即所得是格式的工具列（含「插入圖片或檔案」）；Markdown 模式只有「插入圖片或檔案」；下載好之前沒有，只留切換（高度不變）。 */
  readonly children?: ReactNode;
}

/**
 * 編輯器上方的一列：左邊是格式的工具列（放不下時在自己裡面橫向捲），右邊固定是切換所見即所得與 Markdown 原文。
 * 上傳中、送出中切換用 aria-disabled 停住並把原因寫在旁邊（和 UploadAwareSubmit 一樣）：
 * 所見即所得裡「上傳中」的記號是節點，切過去再切回來會被讀成一般的圖片，傳完就換不掉了。
 */
export function EditorToolbarBar({ control, onShortcutKey, children }: EditorToolbarBarProps) {
  const reasonId = useId();
  const { mode, onChange, disabledReason } = control;
  return (
    <div className={styles.bar}>
      {children}
      <div className={styles.modeSwitch} onKeyDown={onShortcutKey}>
        {disabledReason !== null && (
          <span id={reasonId} className={styles.reason}>
            {disabledReason}
          </span>
        )}
        <SegmentedControl
          label="編輯方式"
          size="sm"
          options={MODE_OPTIONS}
          value={mode}
          onChange={onChange}
          isDisabled={disabledReason !== null}
          describedBy={disabledReason === null ? undefined : reasonId}
        />
      </div>
    </div>
  );
}
