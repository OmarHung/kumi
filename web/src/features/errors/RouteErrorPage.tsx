import { useEffect } from 'react';
import { isRouteErrorResponse, useRouteError } from 'react-router';
import { Button } from '../../components/Button/Button';
import { DocumentTitle } from '../../components/DocumentTitle';
import { cx } from '../../lib/cx';
import { pageErrorReporter } from '../../lib/errorReporting';
import styles from './ErrorPages.module.css';

/**
 * 被錯誤頁接住的例外回報到伺服器（kind: render）：React Router 接住之後不會再傳到 window 的 error，
 * main.tsx 掛的回報聽不到。路由自己的錯誤回應（404 這類）是預期中的結果，不回報；ApiError 由回報那一層濾掉。
 */
function useReportRouteError(): void {
  const error = useRouteError();
  useEffect(() => {
    if (!isRouteErrorResponse(error)) pageErrorReporter().report('render', error);
  }, [error]);
}

/**
 * 畫面本身出錯（程式的問題）時顯示。錯誤內容不給使用者看，請他重新整理；錯誤本身回報到伺服器。
 * 在外框裡（只換掉內容區）也在外框外（整頁）都用它，所以不靠頁列：自己留出上方與左右的空間。
 */
export function RouteErrorPage() {
  useReportRouteError();
  return (
    <section role="alert" className={cx(styles.page, styles.standalone)}>
      <DocumentTitle title="畫面出了問題" />
      <h1 className={styles.title}>畫面出了問題</h1>
      <p className={styles.message}>請重新整理頁面。如果一直發生，請通知管理員。</p>
      <Button
        variant="primary"
        onClick={() => {
          window.location.reload();
        }}
      >
        重新整理
      </Button>
    </section>
  );
}
