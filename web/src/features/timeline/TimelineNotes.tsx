import { Link } from 'react-router';
import { Notice } from '../../components/Notice/Notice';
import { TIMELINE_ITEM_LIMIT } from './useTimelineData';
import styles from './Timeline.module.css';

interface TimelineNotesProps {
  /** 連到表格、只看沒有到期日的那個網址。 */
  readonly undatedHref: string;
  readonly undatedCount: number;
  readonly isUndatedTruncated: boolean;
  readonly isTruncated: boolean;
  readonly isMoreFailed: boolean;
}

/**
 * 時間軸下面的提示：沒有日期的項目、超過上限、載入更多失敗。
 * 放在下面而不是上面：上面多一列會把第一列往下推，超出工作區頁的高度預算（docs/design/08-interaction.md「時間軸」）。
 */
export function TimelineNotes({ undatedHref, undatedCount, isUndatedTruncated, isTruncated, isMoreFailed }: TimelineNotesProps) {
  if (undatedCount === 0 && !isTruncated && !isMoreFailed) return null;
  const limit = TIMELINE_ITEM_LIMIT.toLocaleString('en-US');
  return (
    <div className={styles.notes}>
      {undatedCount > 0 && (
        <p>
          <Link to={undatedHref}>{`${String(undatedCount)} 個${isUndatedTruncated ? '以上的' : ''}項目沒有開始日也沒有到期日`}</Link>
        </p>
      )}
      {isTruncated && <Notice tone="warning">{`這段時間的項目超過 ${limit} 個，只顯示前 ${limit} 個；用篩選縮小範圍。`}</Notice>}
      {isMoreFailed && <Notice tone="danger">後面的項目載入失敗，只顯示已經讀到的。重新整理再試一次。</Notice>}
    </div>
  );
}
