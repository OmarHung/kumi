import { useState } from 'react';
import type { ChangeSetDetail, ProjectDetail } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Button } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { Notice } from '../../components/Notice/Notice';
import type { ChangeSetBlueprints } from '../../domain/changeSetText';
import { impactLines } from '../../domain/impactText';
import type { useChangeSetActions } from './useChangeSetActions';
import styles from './ChangeSets.module.css';

type Actions = ReturnType<typeof useChangeSetActions>;

interface DialogProps {
  readonly detail: ChangeSetDetail;
  readonly names: ChangeSetBlueprints;
  readonly actions: Actions;
  readonly onClose: () => void;
}

const NOT_REVERSIBLE =
  '這個動作不能一鍵還原：舊的藍圖版本、被封存的值與每個項目的活動紀錄都會留著，但要改回來得再送一份變更集。';

/** 套用前再確認一次：說明會動到哪些資料、各多少項目，而且不能一鍵還原。 */
function ApplyDialog({ detail, names, actions, onClose }: DialogProps) {
  const { number, baseVersion } = detail.changeSet;
  const error = actions.apply.isError ? actions.dialogError(actions.apply.error) : null;
  const confirm = () => {
    actions.clearProblem();
    actions.apply.mutate(undefined, {
      onError: (failure) => {
        if (actions.refreshAfterProblem(failure)) onClose();
      },
    });
  };
  const cancel = () => {
    actions.apply.reset();
    onClose();
  };
  return (
    <ConfirmDialog
      title={`套用變更集 #${String(number)}？`}
      description={`藍圖會存成第 ${String(baseVersion + 1)} 版，既有的項目照計畫一次搬移完成。`}
      confirmLabel="套用"
      pendingLabel="套用中…"
      isPending={actions.apply.isPending}
      error={error}
      onConfirm={confirm}
      onCancel={cancel}
    >
      {detail.impacts.length === 0 ? (
        <p className={styles.dialogText}>不會動到既有的項目。</p>
      ) : (
        <ul className={styles.dialogList} aria-label="會動到的資料">
          {impactLines(detail.impacts, names, true).map((line, index) => (
            // 影響的順序由伺服器的計畫決定，不會重排。
            <li key={index}>{line.text}</li>
          ))}
        </ul>
      )}
      <Notice tone="warning" role="none" className={styles.dialogNotice}>
        {NOT_REVERSIBLE}
      </Notice>
    </ConfirmDialog>
  );
}

function RejectDialog({ detail, actions, onClose }: DialogProps) {
  const error = actions.reject.isError ? actions.dialogError(actions.reject.error) : null;
  const confirm = () => {
    actions.clearProblem();
    actions.reject.mutate(undefined, {
      onSuccess: onClose,
      onError: (failure) => {
        if (actions.refreshAfterProblem(failure)) onClose();
      },
    });
  };
  return (
    <ConfirmDialog
      title={`放棄變更集 #${String(detail.changeSet.number)}？`}
      description="放棄之後就不能再套用，要的話得重新送一份。設定不會有任何改變。"
      confirmLabel="放棄"
      pendingLabel="放棄中…"
      tone="danger"
      isPending={actions.reject.isPending}
      error={error}
      onConfirm={confirm}
      onCancel={() => {
        actions.reject.reset();
        onClose();
      }}
    />
  );
}

interface ChangeSetActionsProps {
  readonly project: ProjectDetail;
  readonly detail: ChangeSetDetail;
  readonly names: ChangeSetBlueprints;
  readonly actions: Actions;
}

/** 等待核准時的按鈕：能套用（canApply）才有「套用」；提出的人或有 blueprint.apply 的人有「放棄」。 */
export function ChangeSetActions({ project, detail, names, actions }: ChangeSetActionsProps) {
  const user = useCurrentUser();
  const [open, setOpen] = useState<'apply' | 'reject' | null>(null);
  const { changeSet } = detail;
  // 封存的專案不能套用也不能放棄（伺服器回 409 project.archived）：入口不給。
  const isPending = changeSet.status === 'pending' && project.project.archivedAt === null;
  const canReject =
    isPending && (changeSet.proposedBy.personId === user.personId || project.myPermissions.includes('blueprint.apply'));
  if (!isPending || (!detail.canApply && !canReject)) {
    return null;
  }
  const close = () => {
    setOpen(null);
  };
  return (
    <>
      {canReject && <Button onClick={() => { setOpen('reject'); }}>放棄</Button>}
      {detail.canApply && (
        <Button variant="primary" onClick={() => { setOpen('apply'); }}>
          套用
        </Button>
      )}
      {open === 'apply' && <ApplyDialog detail={detail} names={names} actions={actions} onClose={close} />}
      {open === 'reject' && <RejectDialog detail={detail} names={names} actions={actions} onClose={close} />}
    </>
  );
}
