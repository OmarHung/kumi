import { useState } from 'react';
import { Navigate, Outlet, useLocation } from 'react-router';
import { ErrorState, LoadingState } from '../components/StatusViews/StatusViews';
import { CurrentUserProvider } from '../auth/CurrentUser';
import { loginPathFor, SESSION_EXPIRED_STATE } from '../auth/nextPath';
import { useSession } from '../auth/session';
import styles from './RequireAuth.module.css';

/**
 * 登入後的畫面都放在這底下：沒登入就導到登入頁，並記下原本要去的網址。
 * 這裡已經有過登入的人、之後變成沒登入（任何請求收到 401）時，導過去帶著 SESSION_EXPIRED_STATE，登入頁說明為什麼回到那裡。
 * 第一次打開就沒登入的不帶；自己按登出時帳號選單直接換到登入頁，這裡不會畫到 Navigate，也不帶。
 */
export function RequireAuth() {
  const session = useSession();
  const location = useLocation();
  // 「上一次畫的時候有登入的人」：照 React 的做法在渲染時記下來（不用 effect：導頁就在這一次渲染）。
  const [hadUser, setHadUser] = useState(false);
  if (session.data && !hadUser) {
    setHadUser(true);
  }

  if (session.data) {
    return (
      <CurrentUserProvider user={session.data}>
        <Outlet />
      </CurrentUserProvider>
    );
  }
  if (session.data === null) {
    return <Navigate to={loginPathFor(location)} replace state={hadUser ? SESSION_EXPIRED_STATE : undefined} />;
  }
  return (
    <div className={styles.status}>
      {session.isError ? (
        <ErrorState
          error={session.error}
          title="無法確認登入狀態"
          onRetry={() => {
            void session.refetch();
          }}
        />
      ) : (
        <LoadingState label="正在確認登入狀態…" />
      )}
    </div>
  );
}
