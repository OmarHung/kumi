import { Icon } from '../Icon/Icon';
import styles from './CountBadge.module.css';

export type CountTone = 'neutral' | 'danger';

interface CountBadgeProps {
  readonly value: number;
  /** neutral：一般的數量；danger：要處理的（要修正的問題、已過期），除了顏色還有錯誤的圖示。 */
  readonly tone?: CountTone;
}

/**
 * 名稱旁邊的小計數（分頁的「還沒送出的修改」「要修正的問題」、側欄「我的工作」的數量與過期數）。
 * 畫面上只有數字，aria-hidden：完整的說法放進 CountDescription，用 aria-describedby 連到那個連結或按鈕，名稱維持只有名字。
 */
export function CountBadge({ value, tone = 'neutral' }: CountBadgeProps) {
  return (
    <span className={styles.count} data-tone={tone} aria-hidden="true">
      {tone === 'danger' && <Icon name="error" className={styles.icon} />}
      {value}
    </span>
  );
}

interface CountDescriptionProps {
  readonly id: string;
  /** 每一個計數的完整說法（含數字），用「、」接起來，例如「8 項、2 項已過期」。 */
  readonly labels: readonly string[];
}

/**
 * 計數給報讀軟體的說法：hidden 的元素，只當成說明（aria-describedby 照樣讀得到隱藏的元素），
 * 報讀軟體逐字瀏覽時不會再唸一次。內容是空的時照樣掛著，aria-describedby 不會指到不存在的 id。
 */
export function CountDescription({ id, labels }: CountDescriptionProps) {
  return (
    <span id={id} hidden>
      {labels.join('、')}
    </span>
  );
}
