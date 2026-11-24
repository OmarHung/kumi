import { useUnreadCount } from '../../api/notificationQueries';
import { CountBadge, CountDescription } from '../../components/CountBadge/CountBadge';
import styles from './Inbox.module.css';

/**
 * 側欄「收件匣」旁邊的未讀數（和「我的工作」旁邊的數量同一個長相，CountBadge）：0 或讀不到時不顯示。
 * 完整的說法（「3 則未讀」）放在 hidden 的元素裡，由連結的 aria-describedby 連過來——連結的名稱維持「收件匣」；
 * 元素一直掛著（沒有數字時是空的），aria-describedby 不會指到不存在的 id。
 */
export function InboxCount({ id }: { readonly id: string }) {
  const { data: count } = useUnreadCount();
  const labels = count === undefined || count === 0 ? [] : [`${String(count)} 則未讀`];
  return (
    <>
      {count !== undefined && count > 0 && (
        <span className={styles.sidebarCount} aria-hidden="true" title={labels.join('、')}>
          <CountBadge value={count} />
        </span>
      )}
      <CountDescription id={id} labels={labels} />
    </>
  );
}
