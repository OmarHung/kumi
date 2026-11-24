import { Link } from 'react-router';
import { buttonClassName } from '../../components/Button/Button';
import { StatusPage } from '../../components/StatusPage/StatusPage';
import styles from './ErrorPages.module.css';

interface NotFoundProps {
  /** 大字的狀態碼；沒有權限看的時候是 403。 */
  readonly code?: string;
  readonly title?: string;
  readonly message?: string;
  readonly backTo?: string;
  readonly backLabel?: string;
}

/**
 * 找不到的頁面；專案頁與項目頁在找不到或沒有權限時也用它，換掉文字與返回的地方。
 * 都在外框裡：照樣有頁列（只有分頁的標題），h1 是內容區的大標題。
 */
export function NotFoundPage({
  code = '404',
  title = '找不到這個頁面',
  message = '網址可能打錯了，或是頁面已經不存在。',
  backTo = '/my-work',
  backLabel = '回到我的工作',
}: NotFoundProps) {
  return (
    <StatusPage title={title}>
      <section className={styles.page} aria-labelledby="not-found-heading">
        <p className={styles.code}>{code}</p>
        <h1 id="not-found-heading" className={styles.title}>
          {title}
        </h1>
        <p className={styles.message}>{message}</p>
        <Link to={backTo} className={buttonClassName('secondary')}>
          {backLabel}
        </Link>
      </section>
    </StatusPage>
  );
}
