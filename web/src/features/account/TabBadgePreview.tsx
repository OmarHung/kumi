import { formatDocumentTitle } from '../../components/DocumentTitle';
import { badgedFaviconUrl, badgeFill, TAB_BADGE_COLORS, type TabBadge, type TabBadgePosition, type TabBadgeSize } from '../inbox/faviconBadge';
import styles from './TabBadgePreview.module.css';

const UNREAD_SAMPLE = 3;
const SAMPLE_TITLE = formatDocumentTitle('我的工作', UNREAD_SAMPLE);

const CORNERS: Readonly<Record<TabBadgePosition, string>> = {
  'top-right': '右上角',
  'bottom-right': '右下角',
  'top-left': '左上角',
  'bottom-left': '左下角',
};

const SIZE_LABELS: Readonly<Record<TabBadgeSize, string>> = { small: '小', medium: '中', large: '大' };

function colorName(badge: TabBadge): string {
  return badge.color === 'custom' ? `自訂顏色（${badgeFill(badge)}）` : TAB_BADGE_COLORS[badge.color].label;
}

/** 整張圖是一張圖片（role="img"）：報讀軟體唸這一句，說出圓點現在的樣子。 */
function describe(badge: TabBadge): string {
  const dot = `${CORNERS[badge.position]}有一個${colorName(badge)}的圓點（${SIZE_LABELS[badge.size]}）`;
  return `示意圖：分頁的圖示${dot}，標題前面是未讀數：「${SAMPLE_TITLE}」。`;
}

/**
 * 「分頁的未讀提示」的示意圖：左邊是放大的圖示（分頁上只有 16px，看不清楚位置與大小），右邊是一個分頁的樣子
 * （實際大小的圖示加上帶未讀數的標題）。兩個圖示和真的分頁用同一個產生的圖，不會和實際的不一樣。高度固定，換選項時版面不跳。
 */
export function TabBadgePreview({ badge }: { readonly badge: TabBadge }) {
  const url = badgedFaviconUrl(badge);
  return (
    <div role="img" aria-label={describe(badge)} className={styles.preview}>
      <img src={url} alt="" className={styles.large} />
      <span className={styles.tab}>
        <img src={url} alt="" className={styles.tabIcon} />
        <span className={styles.tabTitle}>{SAMPLE_TITLE}</span>
      </span>
    </div>
  );
}
