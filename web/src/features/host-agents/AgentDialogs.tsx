import { useState, type SubmitEvent } from 'react';
import { errorMessage } from '../../api/http';
import type { HostAgent, HostAgentPairing } from '../../api/hostAgentTypes';
import { useCreateHostAgent, useDeleteHostAgent, useUpdateHostAgent } from '../../api/hostAgents';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import styles from './HostAgents.module.css';

const MAX_AGENT_NAME = 60;
const NAME_EMPTY = '請填名稱';
const NAME_TOO_LONG = `名稱最多 ${String(MAX_AGENT_NAME)} 個字`;

function nameError(name: string): string | undefined {
  const trimmed = name.trim();
  if (trimmed === '') return NAME_EMPTY;
  return trimmed.length > MAX_AGENT_NAME ? NAME_TOO_LONG : undefined;
}

interface NameDialogProps {
  /** null 是新增（成功後交出配對碼）；有值是改名。 */
  readonly agent: HostAgent | null;
  readonly onPaired: (pairing: HostAgentPairing) => void;
  readonly onClose: () => void;
}

/** 名稱表單：新增（成功後交出配對碼）或改名；檢查、送出、失敗的說明。 */
function useAgentNameForm({ agent, onPaired, onClose }: NameDialogProps) {
  const create = useCreateHostAgent();
  const update = useUpdateHostAgent();
  const [name, setName] = useState(agent?.name ?? '');
  const [error, setError] = useState<string | undefined>(undefined);
  const [formError, setFormError] = useState<string | null>(null);
  const isPending = create.isPending || update.isPending;

  function submit() {
    if (isPending) return;
    const invalid = nameError(name);
    setError(invalid);
    if (invalid !== undefined) return;
    setFormError(null);
    const onError = (failure: unknown) => {
      setFormError(errorMessage(failure));
    };
    if (agent !== null) {
      update.mutate({ id: agent.id, input: { name: name.trim(), enabled: agent.state !== 'disabled' } }, { onSuccess: onClose, onError });
      return;
    }
    create.mutate(name.trim(), {
      onSuccess: (pairing) => {
        // 配對碼交給呼叫端的對話框，這裡的 mutation 不留著它。
        create.reset();
        onClose();
        onPaired(pairing);
      },
      onError,
    });
  }
  const change = (value: string) => {
    setName(value);
    setError(undefined);
  };
  return { name, error, formError, isPending, change, submit };
}

/** 新增主機代理（只要一個名稱，成功後換成配對碼的對話框）或改名稱。 */
export function AgentNameDialog(props: NameDialogProps) {
  const { agent, onClose } = props;
  const form = useAgentNameForm(props);
  const isNew = agent === null;

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    form.submit();
  }

  return (
    <Dialog
      title={isNew ? '新增主機代理' : '改主機代理的名稱'}
      description={isNew ? '一台主機一個代理。建立之後會給一次性的配對碼，到主機上輸入它。' : undefined}
      onClose={form.isPending ? () => undefined : onClose}
    >
      <form className={styles.dialogForm} noValidate aria-busy={form.isPending} onSubmit={handleSubmit}>
        <TextField
          label="名稱"
          autoComplete="off"
          autoFocus
          maxLength={MAX_AGENT_NAME + 20}
          placeholder="例如：辦公室主機"
          value={form.name}
          error={form.error}
          onChange={(event) => {
            form.change(event.target.value);
          }}
        />
        <FormAlert message={form.formError} />
        <DialogFormActions
          submitLabel={isNew ? '建立並取得配對碼' : '儲存'}
          pendingLabel={isNew ? '建立中…' : '儲存中…'}
          isPending={form.isPending}
          keepsFocusWhilePending
          onCancel={onClose}
        />
      </form>
    </Dialog>
  );
}

export function RemoveAgentDialog({ agent, onClose }: { readonly agent: HostAgent; readonly onClose: () => void }) {
  const remove = useDeleteHostAgent();
  return (
    <ConfirmDialog
      title={`移除主機代理「${agent.name}」？`}
      description="它的來源會一起刪掉、排隊與執行中的工作取消，成員的模型選單裡也不再有它。主機上的 worker 用舊的憑證會一律被拒絕；要再用就重新新增與配對。用量紀錄保留。"
      confirmLabel="移除"
      pendingLabel="移除中…"
      tone="danger"
      isPending={remove.isPending}
      keepsFocusWhilePending
      error={remove.isError ? errorMessage(remove.error) : null}
      onConfirm={() => {
        remove.mutate(agent.id, { onSuccess: onClose });
      }}
      onCancel={onClose}
    />
  );
}

export function DisableAgentDialog({ agent, onClose }: { readonly agent: HostAgent; readonly onClose: () => void }) {
  const update = useUpdateHostAgent();
  return (
    <ConfirmDialog
      title={`停用主機代理「${agent.name}」？`}
      description="它的來源會停止派工：排隊中的工作取消、執行中的停下，也不能再登入引擎。要再用就選「啟用」。"
      confirmLabel="停用"
      pendingLabel="停用中…"
      tone="danger"
      isPending={update.isPending}
      keepsFocusWhilePending
      error={update.isError ? errorMessage(update.error) : null}
      onConfirm={() => {
        update.mutate({ id: agent.id, input: { name: agent.name, enabled: false } }, { onSuccess: onClose });
      }}
      onCancel={onClose}
    />
  );
}
