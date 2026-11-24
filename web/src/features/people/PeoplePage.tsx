import { useState } from 'react';
import { isApiError } from '../../api/http';
import { usePeople } from '../../api/queries';
import type { Person } from '../../api/types';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { Avatar } from '../../components/Avatar/Avatar';
import { Button } from '../../components/Button/Button';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { NoPermission } from '../../components/StatusViews/NoPermission';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { ORGANIZATION_ROLE_LABELS } from '../../domain/labels';
import { useIsCompact } from '../../lib/useMediaQuery';
import { EmptyValue } from '../work-items/ItemCells';
import { ChangeRoleDialog } from './ChangeRoleDialog';
import { DisablePersonDialog } from './DisablePersonDialog';
import { NewPersonDialog } from './NewPersonDialog';
import { PeopleList } from './PeopleList';
import { PersonActions, type RenderPersonActions } from './PersonActions';
import { PersonName } from './PersonName';
import { usePeopleAdmin, type PeopleAdmin } from './usePeopleAdmin';
import { ResetPasswordDialog } from './ResetPasswordDialog';
import styles from './PeoplePage.module.css';

const HTTP_FORBIDDEN = 403;

/** 第一次載入的骨架：大約一個小組織的人數。 */
const SKELETON_ROWS = 4;

interface PeopleViewProps {
  readonly people: readonly Person[];
  readonly renderActions: RenderPersonActions | undefined;
  /** 正在重新啟用的人：名稱後面寫「啟用中…」。 */
  readonly enablingIds: ReadonlySet<string>;
}

function PeopleTable({ people, renderActions, enablingIds }: PeopleViewProps) {
  return (
    <TableScroll label="人員" layout="page">
      <table className={tableStyles.table}>
        <caption className="visually-hidden">人員</caption>
        <thead>
          <tr>
            <th scope="col">名稱</th>
            <th scope="col">Email</th>
            <th scope="col">組織角色</th>
            {renderActions !== undefined && (
              <th scope="col">
                <span className="visually-hidden">動作</span>
              </th>
            )}
          </tr>
        </thead>
        <tbody>
          {people.map((person, index) => (
            // 第一列標上 data-content-start：給 scripts/ui-audit.js 量第一筆內容離頂端多遠（docs/DESIGN.md 6.2）。
            <tr key={person.personId} data-content-start={index === 0 || undefined}>
              <td className={tableStyles.primaryCell}>
                <span className={styles.name}>
                  <Avatar name={person.displayName} size="sm" decorative />
                  <PersonName person={person} isEnabling={enablingIds.has(person.personId)} />
                </span>
              </td>
              <td className={tableStyles.muted}>{person.email ?? <EmptyValue label="沒有 Email" />}</td>
              <td className={tableStyles.nowrap}>{ORGANIZATION_ROLE_LABELS[person.role]}</td>
              {renderActions !== undefined && <td className={styles.actionCell}>{renderActions(person)}</td>}
            </tr>
          ))}
        </tbody>
      </table>
    </TableScroll>
  );
}

function PeopleContent({ renderActions, enablingIds }: Omit<PeopleViewProps, 'people'>) {
  const people = usePeople();
  const isCompact = useIsCompact();
  if (people.isPending) {
    return <Skeleton label="正在載入人員…" rows={SKELETON_ROWS} />;
  }
  // 只有第一次就讀不到才換成錯誤；已經有清單時重抓失敗，留著清單、多一行提示。
  if (people.isLoadingError) {
    if (isApiError(people.error) && people.error.status === HTTP_FORBIDDEN) {
      return <NoPermission message={people.error.message} />;
    }
    const retry = () => {
      void people.refetch();
    };
    return <ErrorState error={people.error} onRetry={retry} />;
  }
  // 窄螢幕換成清單（換元件，不是把表格擠小：docs/DESIGN.md 6.3）。
  const view = isCompact ? (
    <PeopleList people={people.data} renderActions={renderActions} enablingIds={enablingIds} />
  ) : (
    <PeopleTable people={people.data} renderActions={renderActions} enablingIds={enablingIds} />
  );
  return (
    <>
      <RefetchErrorNotice query={people} message="無法更新人員清單，畫面上可能是舊的。" />
      {view}
    </>
  );
}

/** 變更角色的結果：已經是那個角色時照實說，不寫成「改成」。 */
function roleChangedMessage(before: Person, after: Person): string {
  const role = ORGANIZATION_ROLE_LABELS[after.role];
  return before.role === after.role
    ? `「${after.displayName}」的組織角色已經是「${role}」。`
    : `已把「${after.displayName}」的組織角色改成「${role}」。`;
}

/** 變更角色、停用的確認與重設密碼的對話框：成功時關上，結果寫在頁面上方。 */
function PersonDialogs({ admin }: { readonly admin: PeopleAdmin }) {
  const { dialog, closeDialog, finish } = admin;
  if (dialog === null) return null;
  const { person } = dialog;
  if (dialog.kind === 'changeRole') {
    return <ChangeRoleDialog person={person} onClose={closeDialog} onChanged={(changed) => { finish(roleChangedMessage(person, changed)); }} />;
  }
  return dialog.kind === 'disable' ? (
    <DisablePersonDialog person={person} onClose={closeDialog} onDisabled={() => { finish(`已停用「${person.displayName}」的帳號。`); }} />
  ) : (
    <ResetPasswordDialog person={person} onClose={closeDialog} onReset={() => { finish(`已重設「${person.displayName}」的密碼，他的所有登入都登出了。`); }} />
  );
}

/**
 * 組織裡的人與他們的組織角色。組織的擁有者與管理員可以新增人員，每一列最後有變更角色、停用、重新啟用、重設密碼
 * （internal/HANDOFF-security-local-ops.md 第 7 節第 2 點）；真正的權限由後端檢查。
 */
export function PeoplePage() {
  const user = useCurrentUser();
  const canAdminister = isOrganizationAdmin(user);
  const [isCreating, setIsCreating] = useState(false);
  const admin = usePeopleAdmin();
  const renderActions: RenderPersonActions | undefined = canAdminister
    ? (person) => <PersonActions person={person} {...admin.handlers} />
    : undefined;

  // 沒有權限的人沒有入口（頁列就沒有動作區）。
  const createButton = canAdminister ? (
    <Button
      variant="primary"
      onClick={() => {
        admin.clear();
        setIsCreating(true);
      }}
    >
      新增人員
    </Button>
  ) : undefined;

  return (
    <PageLayout variant="list" bar={<PageBar title="人員" actions={createButton} />}>
      {/* 一直掛著（沒有訊息時看不見、不佔位置）：之後才填進來的訊息，報讀軟體才唸得到。 */}
      <Notice tone="success" role="status" className={styles.status}>
        {admin.message}
      </Notice>
      {admin.failure !== null && (
        <div className={styles.status}>
          <FormAlert message={admin.failure} />
        </div>
      )}
      <PeopleContent renderActions={renderActions} enablingIds={admin.handlers.pendingPersonIds} />
      {isCreating && (
        <NewPersonDialog
          onClose={() => {
            setIsCreating(false);
          }}
          onCreated={(person) => {
            setIsCreating(false);
            admin.finish(`已新增「${person.displayName}」。`);
          }}
        />
      )}
      <PersonDialogs admin={admin} />
    </PageLayout>
  );
}
