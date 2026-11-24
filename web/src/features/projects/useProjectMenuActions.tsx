import { useState, type ReactNode } from 'react';
import { errorMessage } from '../../api/http';
import type { Project } from '../../api/types';
import { Notice } from '../../components/Notice/Notice';
import { RenameProjectDialog } from './RenameProjectDialog';
import { useArchiveProject, useUnarchiveProject } from './useProjectLifecycle';

/** 從頁列的選單封存或取消封存失敗時的說明（成功了不說：頁面上方的封存提示就是結果）。 */
function LifecycleError({ error, onDismiss }: { readonly error: unknown; readonly onDismiss: () => void }) {
  return (
    <Notice tone="danger" onDismiss={onDismiss}>
      {errorMessage(error)}
    </Notice>
  );
}

/**
 * 專案頁「更多」選單的改名、封存、取消封存：給選單的三個動作、要放進頁面提示區的失敗說明、改名的對話框。
 * 封存可以復原，所以選了就做、不確認（docs/design/08-interaction.md「危險的操作」）。
 */
export function useProjectMenuActions(project: Project) {
  const [isRenaming, setIsRenaming] = useState(false);
  const archive = useArchiveProject(project.key);
  const unarchive = useUnarchiveProject(project.key);
  const errors: ReactNode = (
    <>
      {archive.isError && <LifecycleError error={archive.error} onDismiss={archive.reset} />}
      {unarchive.isError && <LifecycleError error={unarchive.error} onDismiss={unarchive.reset} />}
    </>
  );
  const dialog: ReactNode = isRenaming ? (
    <RenameProjectDialog
      projectKey={project.key}
      currentName={project.name}
      onClose={() => {
        setIsRenaming(false);
      }}
    />
  ) : null;
  return {
    errors,
    dialog,
    onRename: () => {
      setIsRenaming(true);
    },
    onArchive: () => {
      archive.mutate();
    },
    onUnarchive: () => {
      unarchive.mutate();
    },
  };
}
