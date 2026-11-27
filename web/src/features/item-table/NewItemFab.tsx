import { Icon } from '../../components/Icon/Icon';
import styles from './NewItemFab.module.css';

/**
 * 窄螢幕的「新增項目」：右下角的圓形主要按鈕（docs/design/08-interaction.md，2026-11-27 使用者看過手機版設計稿後採用）。
 * 工具列一行放不下新增，而且新增是清單頁最常做的事，放在拇指搆得到的地方；表格、看板、行事曆、時間軸都有同一顆。
 * 打開的是 AddItemDialog（對話框關上後焦點回到這顆）。沒有建立權限的人由呼叫端不畫它。
 */
export function NewItemFab({ onOpen }: { readonly onOpen: () => void }) {
  return (
    <button type="button" className={styles.fab} aria-label="新增項目" onClick={onOpen}>
      <Icon name="add" />
    </button>
  );
}
