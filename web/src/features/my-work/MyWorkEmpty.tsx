import { useState } from 'react';
import { Link } from 'react-router';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { Button, buttonClassName } from '../../components/Button/Button';
import type { MyWorkMine } from '../../domain/myWorkView';
import { EmptyState } from '../../components/StatusViews/StatusViews';
import { NewProjectDialog } from '../projects/NewProjectDialog';

/**
 * 我的工作是空的（我負責的與我建立的各說各的）：說這裡會出現什麼，再給下一步（docs/DESIGN.md 8.1 第 3 點、第 11 節）——到專案看看；
 * 能開專案的人（組織的擁有者與管理員）多一顆「新增專案」，當場打開和專案清單同一個對話框（建好之後換到那個專案）。
 * 對話框關上後焦點回到「新增專案」（Dialog 自己做）。
 */
export function MyWorkEmpty({ mine }: { readonly mine: MyWorkMine }) {
  const canCreate = isOrganizationAdmin(useCurrentUser());
  const [isCreating, setIsCreating] = useState(false);
  const actions = (
    <>
      <Link to="/projects" className={buttonClassName('secondary')}>
        到專案看看
      </Link>
      {canCreate && (
        <Button
          onClick={() => {
            setIsCreating(true);
          }}
        >
          新增專案
        </Button>
      )}
    </>
  );
  return (
    <>
      {mine === 'created' ? (
        <EmptyState title="你還沒有建立任何還沒結束的項目" action={actions}>
          你建立的項目還沒結束時，會出現在這裡。
        </EmptyState>
      ) : (
        <EmptyState title="目前沒有你負責的工作" action={actions}>
          有項目指派給你、而且還沒結束時，會出現在這裡。
        </EmptyState>
      )}
      {isCreating && (
        <NewProjectDialog
          onClose={() => {
            setIsCreating(false);
          }}
        />
      )}
    </>
  );
}
