import type { OrganizationRole, Person, SessionUser } from '../../api/types';
import { isOrganizationAdmin } from '../../auth/CurrentUser';

/**
 * 登入的人能不能停用、重新啟用、重設這個人的帳號（internal/HANDOFF-security-local-ops.md 第 2 節第 3、4 點）：
 * 只有擁有者或管理員；擁有者與管理員的帳號只有擁有者能動；不能動自己的帳號（自己的密碼到「我的帳號」改）。
 * 沒有 Email 的人就是沒有登入帳號（伺服器的 Email 是從帳號來的）：沒有東西可以停用或重設。
 * 畫面只是不顯示入口，真正的檢查在伺服器。
 */
export function canManageAccount(actor: SessionUser, person: Person): boolean {
  if (!isOrganizationAdmin(actor) || person.personId === actor.personId || person.email === null) return false;
  return actor.role === 'owner' || (person.role !== 'owner' && person.role !== 'admin');
}

/** 組織角色的四個選項，權限大的在前（變更角色的下拉照這個順序）。 */
export const ORGANIZATION_ROLES: readonly OrganizationRole[] = ['owner', 'admin', 'member', 'guest'];

/**
 * 登入的人能不能變更這個人的組織角色：只有擁有者或管理員；目前是擁有者或管理員的人只有擁有者能動；不能改自己的。
 * 和停用、重設密碼不同，沒有登入帳號的人也能改（角色是組織裡的事）。畫面只是不顯示入口，真正的檢查在伺服器。
 */
export function canChangeRole(actor: SessionUser, person: Person): boolean {
  if (!isOrganizationAdmin(actor) || person.personId === actor.personId) return false;
  return actor.role === 'owner' || (person.role !== 'owner' && person.role !== 'admin');
}

/** 這個角色選不了的原因（短語）；選得了是 undefined。要改成擁有者或管理員，只有擁有者能做。 */
export function roleUnavailableReason(actor: SessionUser, role: OrganizationRole): string | undefined {
  if (actor.role === 'owner') return undefined;
  if (role === 'owner') return '只有擁有者能設定擁有者';
  return role === 'admin' ? '只有擁有者能設定管理員' : undefined;
}

/** 登入的人能不能設定這個人的職稱與負責範圍：擁有者與管理員，任何人都行（包括自己）；不是安全性的設定。畫面只是不顯示入口。 */
export function canEditProfile(actor: SessionUser): boolean {
  return isOrganizationAdmin(actor);
}
