import { useId, useState } from 'react';
import { errorMessage } from '../../api/http';
import { useLocalAgents, useRemoveLocalAgent } from '../../api/personalAi';
import type { LocalAgentMachine } from '../../api/aiTypes';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { IconButton } from '../../components/IconButton/IconButton';
import { List, ListRow } from '../../components/List/List';
import { Menu } from '../../components/Menu/Menu';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { focusableHeading } from './focusHeading';
import { LocalAgentSetup } from './LocalAgentSetup';
import { LOCAL_AGENT_NOTE_AFTER, LOCAL_AGENT_NOTE_BEFORE, LOCAL_ENGINES, enginesText, onlineText } from './localAgentText';
import styles from './LocalAgent.module.css';

function MachineRow({ machine, now, onRemove }: { readonly machine: LocalAgentMachine; readonly now: number; readonly onRemove: (machine: LocalAgentMachine) => void }) {
  return (
    <ListRow
      title={machine.name}
      meta={[
        { key: 'status', label: '狀態', content: onlineText(machine, now) },
        { key: 'engines', label: '引擎', content: enginesText(machine) },
      ]}
      trailing={
        <Menu
          menuLabel={`「${machine.name}」的動作`}
          align="end"
          items={[
            {
              key: 'remove',
              label: '移除',
              icon: 'delete',
              tone: 'danger',
              onSelect: () => {
                onRemove(machine);
              },
            },
          ]}
          renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={`「${machine.name}」的更多動作`} size="sm" />}
        />
      }
    />
  );
}

function RemoveDialog({ machine, onClose }: { readonly machine: LocalAgentMachine; readonly onClose: () => void }) {
  const remove = useRemoveLocalAgent();
  return (
    <ConfirmDialog
      title={`移除「${machine.name}」？`}
      description="它的 Claude Code／Codex 會從模型選單消失。那台電腦上的 kumi agent 還開著的話，下次連線會再登記回來。"
      confirmLabel="移除"
      pendingLabel="移除中…"
      tone="danger"
      isPending={remove.isPending}
      keepsFocusWhilePending
      error={remove.isError ? errorMessage(remove.error) : null}
      onConfirm={() => {
        remove.mutate(machine.id, { onSuccess: onClose });
      }}
      onCancel={onClose}
    />
  );
}

/** 左欄的說明：引擎的名稱整段不斷行（「Claude Code／Codex」原本在斜線處折行）。 */
function LocalAgentNote() {
  return (
    <p>
      {LOCAL_AGENT_NOTE_BEFORE}
      <span className={styles.keepTogether}>{LOCAL_ENGINES}</span>
      {LOCAL_AGENT_NOTE_AFTER}
    </p>
  );
}

/**
 * AI 帳號頁的「本機代理」：自己電腦上的 Claude Code／Codex（internal/HANDOFF-local-agent.md 第 6 節）。
 * 標題用「本機代理」（和按鈕「建立本機代理用的權杖」同一個說法）：「自己電腦上的 Claude Code／Codex」在左欄會從斜線折成兩行，完整的說法寫在說明裡。
 * 有電腦：清單（在線、引擎與版本、「更多」裡的移除要確認）；沒有：一步一步的設定。
 * isAllowed 是組織允許：不允許時（只剩登記過的電腦）只列、只能移除，沒有設定步驟。
 */
export function LocalAgentSection({ isAllowed }: { readonly isAllowed: boolean }) {
  const machines = useLocalAgents();
  const headingId = useId();
  const [removing, setRemoving] = useState<LocalAgentMachine | null>(null);
  const list = machines.data ?? [];
  return (
    <SettingsSection title="本機代理" id="local-agent" headingId={headingId} description={<LocalAgentNote />}>
      {machines.isPending && <Skeleton label="正在載入電腦…" rows={1} />}
      {machines.isLoadingError && (
        <ErrorState
          error={machines.error}
          onRetry={() => {
            void machines.refetch();
          }}
        />
      )}
      <RefetchErrorNotice query={machines} message="無法更新電腦的清單，畫面上可能是舊的。" />
      {list.length > 0 && (
        <List label="我的電腦">
          {list.map((machine) => (
            <MachineRow key={machine.id} machine={machine} now={machines.dataUpdatedAt} onRemove={setRemoving} />
          ))}
        </List>
      )}
      {machines.isSuccess && isAllowed && <LocalAgentSetup hasMachines={list.length > 0} />}
      {removing !== null && (
        <RemoveDialog
          machine={removing}
          onClose={() => {
            setRemoving(null);
            focusableHeading(headingId)?.focus();
          }}
        />
      )}
    </SettingsSection>
  );
}
