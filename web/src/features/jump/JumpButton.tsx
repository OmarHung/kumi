import { Icon } from '../../components/Icon/Icon';
import { ariaKeyShortcuts, formatShortcut, SHORTCUTS } from '../../lib/shortcuts';
import { useJump } from './JumpProvider';
import styles from './JumpButton.module.css';

/**
 * 側欄最上面看得到的入口（docs/DESIGN.md 8.3「快速跳轉」）：名稱只有「搜尋或跳到…」，
 * 快捷鍵的寫法看得到（只給眼睛看，aria-hidden），報讀軟體從 aria-keyshortcuts 知道。
 */
export function JumpButton() {
  const jump = useJump();
  const spec = SHORTCUTS.openJump;
  return (
    <button
      type="button"
      className={styles.button}
      aria-keyshortcuts={ariaKeyShortcuts(spec)}
      onClick={(event) => {
        // Safari 點了按鈕不會把焦點給它：先給，對話框關上時焦點才回得來。
        event.currentTarget.focus();
        jump.open();
      }}
    >
      <Icon name="search" className={styles.icon} />
      <span className={styles.label}>{spec.description}</span>
      <kbd className={styles.keys} aria-hidden="true">
        {formatShortcut(spec)}
      </kbd>
    </button>
  );
}
