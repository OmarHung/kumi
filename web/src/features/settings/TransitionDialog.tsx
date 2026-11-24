import { useId, useState, type SubmitEvent } from 'react';
import type { WorkflowState } from '../../api/blueprint';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { displayName } from '../../domain/settingsNames';
import { baseTransitions, findTransition, updateTransition } from '../../domain/settingsTransitions';
import { Checklist, type ChecklistOption } from './Checklist';
import { useSettingsDialog, useSettingsEditor } from './SettingsContext';
import { requirableFieldOptions, roleOptions } from './transitionLabels';
import styles from './Settings.module.css';

interface TransitionDialogProps {
  readonly workflowKey: string;
  readonly from: WorkflowState;
  readonly to: WorkflowState;
  readonly onClose: () => void;
}

function shown(options: readonly ChecklistOption[], keys: readonly string[] | undefined): readonly string[] {
  return (keys ?? []).filter((key) => options.some((option) => option.key === key));
}

/** 畫面上沒列出來的（被刪除的角色或欄位）留在草稿裡：復原它們時引用跟著回來，送出時才拿掉。 */
function keepHidden(next: readonly string[], options: readonly ChecklistOption[], keys: readonly string[] | undefined): readonly string[] {
  return [...next, ...(keys ?? []).filter((key) => !options.some((option) => option.key === key))];
}

/** 一條轉換的限制：允許的角色（都不勾是不限）與要先填的欄位。按「套用」才寫進草稿，「取消」或 Esc 不改。 */
export function TransitionDialog({ workflowKey, from, to, onClose }: TransitionDialogProps) {
  const editor = useSettingsEditor();
  useSettingsDialog();
  const hintId = useId();
  const transitions = editor.draft.workflows.find((workflow) => workflow.key === workflowKey)?.transitions;
  const transition = findTransition(transitions, from.key, to.key);
  const original = findTransition(baseTransitions(editor.base, workflowKey), from.key, to.key);
  const roles = roleOptions(editor.draft, true);
  // 原本就要求的核心欄位也列出來：在這裡取消之後再打開，還勾得回來。
  const fields = requirableFieldOptions(editor.draft, [...(transition?.requireFields ?? []), ...(original?.requireFields ?? [])]);
  const [chosenRoles, setChosenRoles] = useState(() => shown(roles, transition?.allowedRoles));
  const [chosenFields, setChosenFields] = useState(() => shown(fields, transition?.requireFields));

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const patch = {
      allowedRoles: keepHidden(chosenRoles, roles, transition?.allowedRoles),
      requireFields: keepHidden(chosenFields, fields, transition?.requireFields),
    };
    editor.update((draft) => updateTransition(draft, workflowKey, from.key, to.key, patch));
    onClose();
  }

  return (
    <Dialog title={`從「${displayName(from)}」到「${displayName(to)}」`} description="誰可以走這條轉換、走之前要先填哪些欄位。送出變更之前都還可以改。" onClose={onClose}>
      <form className={styles.dialogForm} noValidate onSubmit={handleSubmit}>
        <Checklist isStacked legend="允許的角色" options={roles} value={chosenRoles} onChange={setChosenRoles} describedBy={`${hintId}-roles`} />
        <p id={`${hintId}-roles`} className={styles.note}>
          都不勾：每個能改變狀態的角色都可以走。標著「不能改變狀態」的角色沒有這個權限，只允許它們的話沒有人走得了。
        </p>
        <Checklist isStacked legend="要先填的欄位" options={fields} value={chosenFields} onChange={setChosenFields} describedBy={`${hintId}-fields`} />
        <p id={`${hintId}-fields`} className={styles.note}>
          要先填的自訂欄位要套用在每個用這個流程的類型；要先填上層項目時，這些類型都要能掛在別的類型底下。
        </p>
        <DialogFormActions submitLabel="套用" pendingLabel="套用" isPending={false} onCancel={onClose} />
      </form>
    </Dialog>
  );
}
