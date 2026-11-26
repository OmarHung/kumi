import type { ReactNode } from 'react';
import type { Person, SessionUser } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { canChangeRole, canEditProfile, canManageAccount } from './personAccess';

/** 每一列最後的動作（只有擁有者與管理員有這一欄）；不能動的那一列是 null。 */
export type RenderPersonActions = (person: Person) => ReactNode;

export interface PersonActionHandlers {
  readonly onEditProfile: (person: Person) => void;
  readonly onChangeRole: (person: Person) => void;
  readonly onResetPassword: (person: Person) => void;
  readonly onDisable: (person: Person) => void;
  readonly onEnable: (person: Person) => void;
  /** 正在重新啟用的人：他們那一列的選單暫時打不開（那一列寫「啟用中…」，PersonName）。 */
  readonly pendingPersonIds: ReadonlySet<string>;
}

function accountItems(person: Person, handlers: PersonActionHandlers): MenuItem[] {
  const reset: MenuItem = { key: 'reset-password', label: '重設密碼…', icon: 'resetPassword', onSelect: () => { handlers.onResetPassword(person); } };
  // 停用會讓他立刻被登出，是這個選單裡最重的動作：放最後、危險色（docs/DESIGN.md 第 7 節的 Menu）。
  // 重新啟用可以再停用回去，不必確認，也不用危險色。
  const toggle: MenuItem = person.disabled
    ? { key: 'enable', label: '重新啟用帳號', icon: 'enableAccount', startsGroup: true, onSelect: () => { handlers.onEnable(person); } }
    : { key: 'disable', label: '停用帳號…', icon: 'disableAccount', tone: 'danger', startsGroup: true, onSelect: () => { handlers.onDisable(person); } };
  return [reset, toggle];
}

/** 編輯職稱放第一個（任何人都能改、不具破壞性），接著變更角色；沒有登入帳號的人只剩這兩項。 */
function actionItems(actor: SessionUser, person: Person, handlers: PersonActionHandlers): MenuItem[] {
  const profile: MenuItem = { key: 'edit-profile', label: '編輯職稱…', icon: 'edit', onSelect: () => { handlers.onEditProfile(person); } };
  const role: MenuItem = { key: 'change-role', label: '變更角色…', icon: 'members', onSelect: () => { handlers.onChangeRole(person); } };
  return [
    ...(canEditProfile(actor) ? [profile] : []),
    ...(canChangeRole(actor, person) ? [role] : []),
    ...(canManageAccount(actor, person) ? accountItems(person, handlers) : []),
  ];
}

/**
 * 人員那一列最後的「更多」（ghost 的圖示按鈕，名稱「{名稱} 的動作」）：編輯職稱、變更角色、重設密碼、停用或重新啟用帳號。
 * 擁有者與管理員每一列都有（至少能編輯職稱）；其他動作不能動的那幾列（自己、管理員看到擁有者或管理員）沒有；伺服器的檢查才算數。
 * 選了之後選單先關上、焦點回到按鈕，才開對話框，所以對話框關上後焦點回到這顆按鈕。
 */
export function PersonActions({ person, ...handlers }: { readonly person: Person } & PersonActionHandlers) {
  const user = useCurrentUser();
  const items = actionItems(user, person, handlers);
  if (items.length === 0) return null;
  const label = `${person.displayName} 的動作`;
  return (
    <Menu
      menuLabel={label}
      align="end"
      items={items}
      isDisabled={handlers.pendingPersonIds.has(person.personId)}
      renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={label} size="sm" />}
    />
  );
}
