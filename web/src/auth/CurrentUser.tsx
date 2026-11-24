import { createContext, use, type ReactNode } from 'react';
import type { SessionUser } from '../api/types';

const CurrentUserContext = createContext<SessionUser | null>(null);

interface CurrentUserProviderProps {
  readonly user: SessionUser;
  readonly children: ReactNode;
}

/**
 * 由登入檢查放在登入後的畫面外面。用 Context 而不是各自讀登入狀態的查詢：
 * 登出時外層先把整個畫面換掉，裡面的元件不會在「沒有使用者」的狀態下多渲染一次。
 */
export function CurrentUserProvider({ user, children }: CurrentUserProviderProps) {
  return <CurrentUserContext value={user}>{children}</CurrentUserContext>;
}

export function useCurrentUser(): SessionUser {
  const user = use(CurrentUserContext);
  if (user === null) {
    throw new Error('useCurrentUser 只能用在登入後的畫面裡。');
  }
  return user;
}

/** 組織的擁有者與管理員：可以開專案、管理人員。畫面只是不顯示入口，權限仍由後端檢查。 */
export function isOrganizationAdmin(user: SessionUser): boolean {
  return user.role === 'owner' || user.role === 'admin';
}
