import { useState } from 'react';
import type { HostAgent, HostSource } from '../../api/hostAgentTypes';
import { useDeleteHostSource } from '../../api/hostAgents';
import { errorMessage } from '../../api/http';
import { Button } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu } from '../../components/Menu/Menu';
import { engineLabel, sourceStatusText, usageSummary } from './hostAgentText';
import { SourceDialog } from './SourceDialog';
import { UsageDialog } from './UsageDialog';
import { TextWithCommands } from './CommandText';
import styles from './HostAgents.module.css';

type Dialogs =
  | { readonly type: 'edit'; readonly source: HostSource | null }
  | { readonly type: 'usage'; readonly source: HostSource }
  | { readonly type: 'delete'; readonly source: HostSource }
  | null;

function SourceInfo({ source, agent }: { readonly source: HostSource; readonly agent: HostAgent | undefined }) {
  const enabledCount = source.models.filter((model) => model.enabled).length;
  return (
    <div className={styles.info}>
      <p className={styles.name}>
        {source.name}
        <span className={styles.status}>
          <TextWithCommands text={sourceStatusText(source)} />
        </span>
      </p>
      <p className={styles.note}>{`${agent?.name ?? '（已移除的代理）'} · ${engineLabel(source.engine)} · 開放 ${String(enabledCount)} 個模型`}</p>
      <p className={styles.note}>{`執行中 ${String(source.running)} 件 · 排隊 ${String(source.queued)} 件`}</p>
      <p className={styles.note}>{`本月 ${usageSummary(source.usage)}`}</p>
    </div>
  );
}

function DeleteSourceDialog({ source, onClose }: { readonly source: HostSource; readonly onClose: () => void }) {
  const remove = useDeleteHostSource();
  return (
    <ConfirmDialog
      title={`刪除來源「${source.name}」？`}
      description="成員的模型選單裡不再有它，排隊與執行中的工作取消。已經有的對話留著；用量紀錄保留。"
      confirmLabel="刪除"
      pendingLabel="刪除中…"
      tone="danger"
      isPending={remove.isPending}
      keepsFocusWhilePending
      error={remove.isError ? errorMessage(remove.error) : null}
      onConfirm={() => {
        remove.mutate(source.id, { onSuccess: onClose });
      }}
      onCancel={onClose}
    />
  );
}

interface SourceRowProps {
  readonly source: HostSource;
  readonly agent: HostAgent | undefined;
  readonly onOpen: (dialog: NonNullable<Dialogs>) => void;
}

function SourceRow({ source, agent, onOpen }: SourceRowProps) {
  return (
    <li className={styles.row}>
      <div className={styles.rowMain}>
        <SourceInfo source={source} agent={agent} />
        <div className={styles.actions}>
          <Button size="sm" aria-label={`編輯 ${source.name}`} onClick={() => { onOpen({ type: 'edit', source }); }}>
            編輯
          </Button>
          <Menu
            menuLabel={`${source.name} 的動作`}
            align="end"
            items={[
              { key: 'usage', label: '用量', onSelect: () => { onOpen({ type: 'usage', source }); } },
              { key: 'delete', label: '刪除', icon: 'delete', tone: 'danger', startsGroup: true, onSelect: () => { onOpen({ type: 'delete', source }); } },
            ]}
            renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={`「${source.name}」的更多動作`} size="sm" />}
          />
        </div>
      </div>
    </li>
  );
}

interface SourceListProps {
  readonly agents: readonly HostAgent[];
  readonly sources: readonly HostSource[];
}

/** 來源的清單與新增：名稱、狀態、執行中與排隊的件數、這個月的用量；「編輯」加「更多」（用量、刪除）。 */
export function SourceList({ agents, sources }: SourceListProps) {
  const [dialog, setDialog] = useState<Dialogs>(null);
  const close = () => {
    setDialog(null);
  };
  return (
    <>
      {sources.length === 0 ? (
        <p className={styles.note}>還沒有來源。新增一個，成員才選得到主機上的 Claude Code／Codex。</p>
      ) : (
        <ul className={styles.list} aria-label="主機來源">
          {sources.map((source) => (
            <SourceRow key={source.id} source={source} agent={agents.find((agent) => agent.id === source.agentId)} onOpen={setDialog} />
          ))}
        </ul>
      )}
      <div>
        <Button
          aria-disabled={agents.length === 0 || undefined}
          onClick={() => {
            if (agents.length > 0) setDialog({ type: 'edit', source: null });
          }}
        >
          新增來源
        </Button>
        {agents.length === 0 && <p className={styles.note}>先新增一台主機代理。</p>}
      </div>
      {dialog?.type === 'edit' && <SourceDialog key={dialog.source?.id ?? 'new'} source={dialog.source} agents={agents} sources={sources} onClose={close} />}
      {dialog?.type === 'usage' && <UsageDialog source={dialog.source} onClose={close} />}
      {dialog?.type === 'delete' && <DeleteSourceDialog source={dialog.source} onClose={close} />}
    </>
  );
}
