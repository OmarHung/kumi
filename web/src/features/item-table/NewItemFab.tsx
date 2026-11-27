import { Icon } from '../../components/Icon/Icon';
import styles from './NewItemFab.module.css';

interface NewItemFabProps {
  readonly onOpen: () => void;
  /**
   * round：只有加號的圓形（預設，手機寬度）；extended：加號加「新增項目」的膠囊（平板寬度，有地方放字就寫出來）。
   * 名稱都是「新增項目」：圓形靠 aria-label，膠囊靠看得到的字（名稱就是字）。
   */
  readonly variant?: 'round' | 'extended';
}

/**
 * 窄螢幕的「新增項目」：右下角的主要按鈕（docs/design/08-interaction.md，2026-11-27 使用者看過手機版設計稿後採用）。
 * 工具列一行放不下新增，而且新增是清單頁最常做的事，放在拇指搆得到的地方；表格、看板、行事曆、時間軸都有同一顆。
 * 打開的是 AddItemDialog（對話框關上後焦點回到這顆）。沒有建立權限的人由呼叫端不畫它。
 */
export function NewItemFab({ onOpen, variant = 'round' }: NewItemFabProps) {
  return (
    <button type="button" className={styles.fab} data-variant={variant} aria-label={variant === 'round' ? '新增項目' : undefined} onClick={onOpen}>
      <Icon name="add" />
      {variant === 'extended' && <span className={styles.label}>新增項目</span>}
    </button>
  );
}
