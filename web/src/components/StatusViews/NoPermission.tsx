import { Link } from 'react-router';
import { buttonClassName } from '../Button/Button';
import { EmptyState } from './StatusViews';

/** 這一頁沒有權限（後端回 403，或只有管理員的頁面被別人打了網址）：不是暫時的錯，重試也沒用，給一條回去的路。 */
export function NoPermission({ message }: { readonly message: string }) {
  const back = (
    <Link to="/my-work" className={buttonClassName('secondary')}>
      回到我的工作
    </Link>
  );
  return (
    <EmptyState title="沒有權限" action={back}>
      {message}
    </EmptyState>
  );
}
