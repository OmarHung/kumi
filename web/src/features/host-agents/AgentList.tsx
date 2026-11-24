import { useState } from 'react';
import type { HostAgent, HostAgentPairing } from '../../api/hostAgentTypes';
import { useRepairHostAgent, useUpdateHostAgent } from '../../api/hostAgents';
import { errorMessage } from '../../api/http';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu } from '../../components/Menu/Menu';
import { Notice } from '../../components/Notice/Notice';
import { AgentNameDialog, DisableAgentDialog, RemoveAgentDialog } from './AgentDialogs';
import { EngineRow } from './EngineRow';
import { agentStatusText, HOST_ENGINES } from './hostAgentText';
import styles from './HostAgents.module.css';

type Dialog = 'rename' | 'disable' | 'remove' | null;

function AgentInfo({ agent, now }: { readonly agent: HostAgent; readonly now: number }) {
  const details = [
    agent.agentVersion === null ? null : `版本 ${agent.agentVersion}`,
    agent.machineName,
  ].filter((part) => part !== null && part !== '');
  return (
    <div className={styles.info}>
      <p className={styles.name}>
        {agent.name}
        <span className={styles.status}>{agentStatusText(agent, now)}</span>
      </p>
      {details.length > 0 && <p className={styles.note}>{details.join(' · ')}</p>}
    </div>
  );
}

interface AgentMenuProps {
  readonly agent: HostAgent;
  readonly onRename: () => void;
  readonly onRemove: () => void;
  readonly onDisable: () => void;
  /** 每個動作開始時（上一次的失敗說明先收掉）。 */
  readonly onAction: () => void;
  readonly onPaired: (pairing: HostAgentPairing) => void;
  /** 動作失敗的說明交給上一層顯示（選單關上之後還看得到）。 */
  readonly onError: (error: unknown) => void;
}

/** 「更多」：改名稱、重新配對（拿到新的配對碼）、停用／啟用、移除（要確認）。 */
function AgentMenu({ agent, onRename, onRemove, onDisable, onAction, onPaired, onError }: AgentMenuProps) {
  const repair = useRepairHostAgent();
  const update = useUpdateHostAgent();
  const isDisabled = agent.state === 'disabled';
  return (
    <Menu
      menuLabel={`「${agent.name}」的動作`}
      align="end"
      items={[
        { key: 'rename', label: '改名稱', onSelect: () => { onAction(); onRename(); } },
        {
          key: 'repair',
          label: '重新配對',
          onSelect: () => {
            onAction();
            repair.mutate(agent.id, {
              onSuccess: (pairing) => {
                onPaired(pairing);
                repair.reset();
              },
              onError,
            });
          },
        },
        {
          key: 'toggle',
          label: isDisabled ? '啟用' : '停用',
          onSelect: () => {
            onAction();
            // 啟用直接做；停用會取消排隊、停下執行中的工作，先確認。
            if (isDisabled) update.mutate({ id: agent.id, input: { name: agent.name, enabled: true } }, { onError });
            else onDisable();
          },
        },
        { key: 'remove', label: '移除', icon: 'delete', tone: 'danger', startsGroup: true, onSelect: () => { onAction(); onRemove(); } },
      ]}
      renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={`「${agent.name}」的更多動作`} size="sm" />}
    />
  );
}

interface AgentRowProps {
  readonly agent: HostAgent;
  readonly now: number;
  readonly onPaired: (pairing: HostAgentPairing) => void;
}

/** 一台代理：名稱、狀態、版本與「更多」；底下是兩個引擎。 */
function AgentRow({ agent, now, onPaired }: AgentRowProps) {
  const [dialog, setDialog] = useState<Dialog>(null);
  const [error, setError] = useState<string | null>(null);
  const close = () => {
    setDialog(null);
  };

  return (
    <li className={styles.row}>
      <div className={styles.rowMain}>
        <AgentInfo agent={agent} now={now} />
        <div className={styles.actions}>
          <AgentMenu
            agent={agent}
            onRename={() => { setDialog('rename'); }}
            onRemove={() => { setDialog('remove'); }}
            onDisable={() => { setDialog('disable'); }}
            onAction={() => { setError(null); }}
            onPaired={onPaired}
            onError={(failure) => { setError(errorMessage(failure)); }}
          />
        </div>
      </div>
      {agent.state === 'awaiting_pairing' && <p className={styles.note}>還沒配對：在主機上輸入配對碼。碼過期了就用「重新配對」拿新的。</p>}
      {agent.state === 'disabled' && <p className={styles.note}>已停用：它的來源不接工作，也不能登入引擎。</p>}
      {error !== null && <Notice tone="danger" onDismiss={() => { setError(null); }} dismissLabel={`知道了（${agent.name}）`}>{error}</Notice>}
      {agent.state !== 'awaiting_pairing' && (
        <ul className={styles.engines} aria-label={`${agent.name} 的引擎`}>
          {HOST_ENGINES.map((engine) => (
            <EngineRow key={engine} agent={agent} engine={engine} />
          ))}
        </ul>
      )}
      {dialog === 'rename' && <AgentNameDialog agent={agent} onPaired={onPaired} onClose={close} />}
      {dialog === 'disable' && <DisableAgentDialog agent={agent} onClose={close} />}
      {dialog === 'remove' && <RemoveAgentDialog agent={agent} onClose={close} />}
    </li>
  );
}

interface AgentListProps {
  readonly agents: readonly HostAgent[];
  /** 最近一次讀到清單的時間：「最後在 N 分鐘前」用它算。 */
  readonly now: number;
  readonly onPaired: (pairing: HostAgentPairing) => void;
}

export function AgentList({ agents, now, onPaired }: AgentListProps) {
  if (agents.length === 0) return <p className={styles.note}>還沒有主機代理。先新增一台，再到主機上配對。</p>;
  return (
    <ul className={styles.list} aria-label="主機代理">
      {agents.map((agent) => (
        <AgentRow key={agent.id} agent={agent} now={now} onPaired={onPaired} />
      ))}
    </ul>
  );
}
