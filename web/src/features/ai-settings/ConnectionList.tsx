import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import type { AiConnection, AiConnectionScope } from '../../api/aiTypes';
import { Button } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu } from '../../components/Menu/Menu';
import { Notice } from '../../components/Notice/Notice';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { errorMessage, isApiError } from '../../api/http';
import { PROVIDER_LABELS } from '../ai/aiLabels';
import { useConnectionRowApi } from './connectionApi';
import { ConnectionDialog } from './ConnectionDialog';
import styles from './AiSettings.module.css';

const HTTP_TOO_MANY_REQUESTS = 429;
const RATE_LIMITED = '請求太頻繁，請稍後再試。';
const NUMBER = new Intl.NumberFormat('en-US');

function usageText(connection: AiConnection): string {
  const { replies, inputTokens, outputTokens } = connection.usage;
  return `本月 ${NUMBER.format(replies)} 則回覆 · 輸入 ${NUMBER.format(inputTokens)} · 輸出 ${NUMBER.format(outputTokens)} token`;
}

/** 一個連線的摘要：名稱、供應商、金鑰的最後四個字、開放幾個模型、本月的用量。 */
function ConnectionSummary({ connection }: { readonly connection: AiConnection }) {
  const enabledCount = connection.models.filter((model) => model.enabled).length;
  const key = connection.hasApiKey ? `金鑰 …${connection.apiKeyHint ?? ''}` : '沒有金鑰';
  return (
    <div className={styles.connectionInfo}>
      <p className={styles.connectionName}>
        {connection.name}
        {/* 名稱就是供應商的名稱（新增時預先填的）時不重複寫。 */}
        {connection.name !== PROVIDER_LABELS[connection.kind] && <span className={styles.connectionKind}>{PROVIDER_LABELS[connection.kind]}</span>}
      </p>
      <p className={styles.note}>{`${key} · 開放 ${String(enabledCount)} 個模型`}</p>
      <p className={styles.note}>{usageText(connection)}</p>
    </div>
  );
}

type RefreshResult = { readonly tone: 'success' | 'danger'; readonly text: string };

/** 一個連線的動作：「編輯」加「更多」（重新讀取、刪除）；只能刪的時候只有「刪除」。 */
function RowActions({ connection, isDeleteOnly, isRefreshing, onEdit, onRefresh, onDelete }: {
  readonly connection: AiConnection;
  readonly isDeleteOnly: boolean;
  readonly isRefreshing: boolean;
  readonly onEdit: () => void;
  readonly onRefresh: () => void;
  readonly onDelete: () => void;
}) {
  if (isDeleteOnly) {
    return (
      <div className={styles.connectionActions}>
        <Button variant="ghost" size="sm" onClick={onDelete} aria-label={`刪除 ${connection.name}`}>
          刪除
        </Button>
      </div>
    );
  }
  return (
    <div className={styles.connectionActions}>
      <Button size="sm" onClick={onEdit} aria-label={`編輯 ${connection.name}`}>
        編輯
      </Button>
      {/* 不常做、或要確認的動作收進「更多」：紅色實心只留給確認對話框（docs/design/07-components.md）。 */}
      <Menu
        menuLabel={`${connection.name} 的動作`}
        align="end"
        items={[
          {
            key: 'refresh',
            label: isRefreshing ? '讀取中…' : '重新讀取模型',
            disabledReason: isRefreshing ? '正在重新讀取' : undefined,
            onSelect: onRefresh,
          },
          { key: 'delete', label: '刪除', icon: 'delete', tone: 'danger', startsGroup: true, onSelect: onDelete },
        ]}
        renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={`「${connection.name}」的更多動作`} size="sm" />}
      />
    </div>
  );
}

interface ConnectionRowProps {
  readonly scope: AiConnectionScope;
  readonly connection: AiConnection;
  /** 只能刪（組織不再允許自己的 AI 帳號時）：不給編輯與重新讀取。 */
  readonly isDeleteOnly: boolean;
  readonly onEdit: () => void;
  readonly onDelete: () => void;
}

function ConnectionRow({ scope, connection, isDeleteOnly, onEdit, onDelete }: ConnectionRowProps) {
  const { refresh } = useConnectionRowApi(scope);
  const [result, setResult] = useState<RefreshResult | null>(null);

  function handleRefresh() {
    if (refresh.isPending) return;
    setResult(null);
    refresh.mutate(connection.id, {
      onSuccess: (saved) => {
        const enabled = saved.models.filter((model) => model.enabled).length;
        setResult({ tone: 'success', text: `已重新讀取，共 ${String(saved.models.length)} 個模型，開放 ${String(enabled)} 個。` });
      },
      onError: (error) => {
        const isRateLimited = isApiError(error) && error.status === HTTP_TOO_MANY_REQUESTS;
        setResult({ tone: 'danger', text: isRateLimited ? RATE_LIMITED : errorMessage(error) });
      },
    });
  }

  return (
    <li className={styles.connection}>
      <div className={styles.connectionMain}>
        <ConnectionSummary connection={connection} />
        <RowActions
          connection={connection}
          isDeleteOnly={isDeleteOnly}
          isRefreshing={refresh.isPending}
          onEdit={onEdit}
          onRefresh={handleRefresh}
          onDelete={onDelete}
        />
      </div>
      {result !== null && (
        <Notice tone={result.tone} onDismiss={() => { setResult(null); }} dismissLabel={`知道了（${connection.name}）`}>
          {result.text}
        </Notice>
      )}
    </li>
  );
}

function DeleteConnectionDialog({ scope, connection, onClose }: { readonly scope: AiConnectionScope; readonly connection: AiConnection; readonly onClose: () => void }) {
  const { remove } = useConnectionRowApi(scope);
  const [error, setError] = useState<string | null>(null);
  return (
    <ConfirmDialog
      title={`刪除連線「${connection.name}」？`}
      description={scope === 'organization' ? '已經有的對話留著；它是預設時，AI 會一起關掉。' : '已經有的對話留著；之後不能再用這個帳號回覆。'}
      confirmLabel="刪除"
      pendingLabel="刪除中…"
      tone="danger"
      isPending={remove.isPending}
      keepsFocusWhilePending
      error={error}
      onConfirm={() => {
        setError(null);
        remove.mutate(connection.id, { onSuccess: onClose, onError: (failure) => { setError(errorMessage(failure)); } });
      }}
      onCancel={onClose}
    />
  );
}

type Dialogs = { readonly type: 'edit'; readonly connection: AiConnection | null } | { readonly type: 'delete'; readonly connection: AiConnection } | null;

/**
 * 登入回來的頁面把連線編號放在網址（?edit=…、?aiConnection=…）：這一頁一打開就直接開那個連線的編輯對話框（讓使用者勾開放的模型），
 * 然後把參數拿掉（重新整理不會又打開）。清單是載入完才掛上的，所以初值就看得到；找不到的編號（已經刪了）不打開，參數照樣拿掉。
 */
function useOpenFromParam(param: string, connections: readonly AiConnection[], canEdit: boolean) {
  const [params, setParams] = useSearchParams();
  const requestedId = params.get(param);
  const [initial] = useState<Dialogs>(() => {
    const found = canEdit && requestedId !== null ? connections.find((connection) => connection.id === requestedId) : undefined;
    return found === undefined ? null : { type: 'edit', connection: found };
  });
  useEffect(() => {
    if (requestedId === null) return;
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        next.delete(param);
        return next;
      },
      { replace: true },
    );
  }, [param, requestedId, setParams]);
  return initial;
}

interface ConnectionManagerProps {
  readonly scope: AiConnectionScope;
  readonly connections: readonly AiConnection[];
  /** 沒有連線時的說明。 */
  readonly emptyText: string;
  /** 網址上帶連線編號的參數名稱（登入回來後打開編輯對話框）。 */
  readonly openParam: string;
  /** 不能新增也不能編輯、只能刪（組織不允許自己的 AI 帳號）。 */
  readonly isDeleteOnly?: boolean;
  readonly addLabel: string;
}

/** 連線的清單、每個連線的動作與新增（組織與自己的共用，差別只在 scope）。 */
export function ConnectionManager({ scope, connections, emptyText, openParam, isDeleteOnly = false, addLabel }: ConnectionManagerProps) {
  const initialDialog = useOpenFromParam(openParam, connections, !isDeleteOnly);
  const [dialog, setDialog] = useState<Dialogs>(initialDialog);
  const close = () => {
    setDialog(null);
  };
  return (
    <>
      {connections.length === 0 ? (
        <p className={styles.note}>{emptyText}</p>
      ) : (
        <ul className={styles.connections}>
          {connections.map((connection) => (
            <ConnectionRow
              key={connection.id}
              scope={scope}
              connection={connection}
              isDeleteOnly={isDeleteOnly}
              onEdit={() => { setDialog({ type: 'edit', connection }); }}
              onDelete={() => { setDialog({ type: 'delete', connection }); }}
            />
          ))}
        </ul>
      )}
      {!isDeleteOnly && (
        <div>
          <Button onClick={() => { setDialog({ type: 'edit', connection: null }); }}>{addLabel}</Button>
        </div>
      )}
      {dialog?.type === 'edit' && <ConnectionDialog key={dialog.connection?.id ?? 'new'} scope={scope} connection={dialog.connection} onClose={close} />}
      {dialog?.type === 'delete' && <DeleteConnectionDialog scope={scope} connection={dialog.connection} onClose={close} />}
    </>
  );
}

/** AI 設定頁的「連線」一塊（組織的）。 */
export function ConnectionList({ connections, description }: { readonly connections: readonly AiConnection[]; readonly description: string }) {
  return (
    <SettingsSection title="連線" description={<p>{description}</p>}>
      <div className={styles.connectionsBody}>
        <ConnectionManager
          scope="organization"
          connections={connections}
          emptyText="還沒有連線。新增一個供應商的連線，並開放至少一個模型。"
          openParam="edit"
          addLabel="新增連線"
        />
      </div>
    </SettingsSection>
  );
}
