import type { Milestone, MilestoneDraft, PlanningMutations } from '../../api/planning';
import type { CalendarDate } from '../../api/types';
import { DeletePlanningDialog } from './DeletePlanningDialog';
import type { RowDialog } from './dialogState';
import { MilestoneDialog } from './MilestoneDialog';

function milestoneDraftOf(milestone: Milestone): MilestoneDraft {
  return { name: milestone.name, dueDate: milestone.dueDate, description: milestone.description };
}

/** 關上時清掉上一次的失敗：mutation 的狀態屬於 hook，不是對話框，不清的話下一次開啟會先看到舊的錯誤。 */
function closeAfterReset(reset: () => void, onClose: () => void): () => void {
  return () => {
    reset();
    onClose();
  };
}

interface MilestoneDialogsProps {
  readonly dialog: RowDialog<Milestone> | null;
  readonly today: CalendarDate;
  readonly mutations: PlanningMutations;
  readonly onClose: () => void;
}

interface FormDialogProps {
  readonly mutations: PlanningMutations;
  readonly onClose: () => void;
}

/** 到期日預設今天。 */
function CreateMilestoneDialog({ today, mutations, onClose }: FormDialogProps & { readonly today: CalendarDate }) {
  const { createMilestone } = mutations;
  return (
    <MilestoneDialog
      title="新增里程碑"
      submitLabel="新增"
      initial={{ name: '', dueDate: today, description: null }}
      isPending={createMilestone.isPending}
      error={createMilestone.error}
      onEdit={createMilestone.reset}
      onSubmit={(draft) => {
        createMilestone.mutate(draft, { onSuccess: onClose });
      }}
      onClose={closeAfterReset(createMilestone.reset, onClose)}
    />
  );
}

function EditMilestoneDialog({ milestone, mutations, onClose }: FormDialogProps & { readonly milestone: Milestone }) {
  const { replaceMilestone } = mutations;
  return (
    <MilestoneDialog
      title="編輯里程碑"
      submitLabel="儲存"
      initial={milestoneDraftOf(milestone)}
      isPending={replaceMilestone.isPending}
      error={replaceMilestone.error}
      onEdit={replaceMilestone.reset}
      onSubmit={(draft) => {
        replaceMilestone.mutate({ id: milestone.id, draft }, { onSuccess: onClose });
      }}
      onClose={closeAfterReset(replaceMilestone.reset, onClose)}
    />
  );
}

/** 里程碑的新增、編輯、刪除對話框；成功就關，失敗留著、原因在對話框裡。 */
export function MilestoneDialogs({ dialog, today, mutations, onClose }: MilestoneDialogsProps) {
  if (dialog === null) return null;
  if (dialog.kind === 'create') return <CreateMilestoneDialog today={today} mutations={mutations} onClose={onClose} />;
  if (dialog.kind === 'edit') return <EditMilestoneDialog milestone={dialog.row} mutations={mutations} onClose={onClose} />;
  const { deleteMilestone } = mutations;
  return (
    <DeletePlanningDialog
      name={dialog.row.name}
      kind="里程碑"
      itemCount={dialog.row.progress.total}
      isPending={deleteMilestone.isPending}
      error={deleteMilestone.error}
      onConfirm={() => {
        deleteMilestone.mutate(dialog.row.id, { onSuccess: onClose });
      }}
      onClose={closeAfterReset(deleteMilestone.reset, onClose)}
    />
  );
}
