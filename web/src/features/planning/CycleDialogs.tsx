import type { Cycle, CycleDraft, PlanningMutations } from '../../api/planning';
import type { CalendarDate } from '../../api/types';
import { CycleDialog } from './CycleDialog';
import { DeletePlanningDialog } from './DeletePlanningDialog';
import type { RowDialog } from './dialogState';
import { defaultCycleDraft } from './planningRules';

function cycleDraftOf(cycle: Cycle): CycleDraft {
  return { name: cycle.name, startDate: cycle.startDate, endDate: cycle.endDate };
}

/** 關上時清掉上一次的失敗：mutation 的狀態屬於 hook，不是對話框，不清的話下一次開啟會先看到舊的錯誤。 */
function closeAfterReset(reset: () => void, onClose: () => void): () => void {
  return () => {
    reset();
    onClose();
  };
}

interface CycleDialogsProps {
  readonly dialog: RowDialog<Cycle> | null;
  readonly cycles: readonly Cycle[];
  readonly today: CalendarDate;
  readonly mutations: PlanningMutations;
  readonly onClose: () => void;
}

interface FormDialogProps {
  readonly mutations: PlanningMutations;
  readonly onClose: () => void;
}

function CreateCycleDialog({ cycles, today, mutations, onClose }: FormDialogProps & { readonly cycles: readonly Cycle[]; readonly today: CalendarDate }) {
  const { createCycle } = mutations;
  return (
    <CycleDialog
      title="新增週期"
      submitLabel="新增"
      initial={defaultCycleDraft(cycles, today)}
      isPending={createCycle.isPending}
      error={createCycle.error}
      onEdit={createCycle.reset}
      onSubmit={(draft) => {
        createCycle.mutate(draft, { onSuccess: onClose });
      }}
      onClose={closeAfterReset(createCycle.reset, onClose)}
    />
  );
}

function EditCycleDialog({ cycle, mutations, onClose }: FormDialogProps & { readonly cycle: Cycle }) {
  const { replaceCycle } = mutations;
  return (
    <CycleDialog
      title="編輯週期"
      submitLabel="儲存"
      initial={cycleDraftOf(cycle)}
      isPending={replaceCycle.isPending}
      error={replaceCycle.error}
      onEdit={replaceCycle.reset}
      onSubmit={(draft) => {
        replaceCycle.mutate({ id: cycle.id, draft }, { onSuccess: onClose });
      }}
      onClose={closeAfterReset(replaceCycle.reset, onClose)}
    />
  );
}

/** 週期的新增、編輯、刪除對話框；成功就關，失敗留著、原因在對話框裡。 */
export function CycleDialogs({ dialog, cycles, today, mutations, onClose }: CycleDialogsProps) {
  if (dialog === null) return null;
  if (dialog.kind === 'create') return <CreateCycleDialog cycles={cycles} today={today} mutations={mutations} onClose={onClose} />;
  if (dialog.kind === 'edit') return <EditCycleDialog cycle={dialog.row} mutations={mutations} onClose={onClose} />;
  const { deleteCycle } = mutations;
  return (
    <DeletePlanningDialog
      name={dialog.row.name}
      kind="週期"
      itemCount={dialog.row.progress.total}
      isPending={deleteCycle.isPending}
      error={deleteCycle.error}
      onConfirm={() => {
        deleteCycle.mutate(dialog.row.id, { onSuccess: onClose });
      }}
      onClose={closeAfterReset(deleteCycle.reset, onClose)}
    />
  );
}
