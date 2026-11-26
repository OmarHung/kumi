import { useEffect, useId, useRef, useState } from 'react';
import type { LocalEngineKind } from '../../api/aiTypes';
import type { HostAgent, HostEngineStatus, HostLogin, HostLoginStart } from '../../api/hostAgentTypes';
import { useCancelHostLogin, useLogoutHostEngine, useStartHostLogin } from '../../api/hostAgents';
import { errorMessage } from '../../api/http';
import { Button } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { CodeDialog } from './CodeDialog';
import { BILLING_TEXT, engineLabel, loginCommand, loginRule, READINESS_TEXT } from './hostAgentText';
import { TextWithCommands } from './CommandText';
import styles from './HostAgents.module.css';

const LOGIN_STATE_TEXT = {
  pending: '等待在主機輸入登入碼',
  claimed: '主機已收到登入碼，等待完成登入',
} as const;

/**
 * 還沒結束的登入，和最近失敗的那一次（失敗的原因要讓擁有者看到）。
 * 失敗之後已經有一次成功的授權時不再顯示那次失敗：紅字留著會讓擁有者以為還有問題。
 */
function currentLogin(agent: HostAgent, engine: LocalEngineKind, authorizedAt: string | null): HostLogin | undefined {
  const mine = agent.logins.filter((login) => login.engine === engine);
  const isAfterAuthorization = (login: HostLogin) => authorizedAt === null || Date.parse(login.createdAt) > Date.parse(authorizedAt);
  return (
    mine.find((login) => login.state === 'pending' || login.state === 'claimed') ??
    mine.find((login) => login.state === 'failed' && isAfterAuthorization(login))
  );
}

function EngineFacts({ status }: { readonly status: HostEngineStatus }) {
  const parts = [
    status.version === null ? null : `版本 ${status.version}`,
    status.loggedIn ? BILLING_TEXT[status.billing] : null,
    status.authorizedBy === null || status.authorizedAt === null ? null : `${status.authorizedBy.displayName} 在 ${formatInstant(status.authorizedAt)} 授權`,
  ].filter((part) => part !== null);
  return parts.length === 0 ? null : <p className={styles.note}>{parts.join(' · ')}</p>;
}

function statusText(status: HostEngineStatus | undefined): string {
  if (status === undefined) return '還沒回報';
  return status.logoutPending ? '登出中' : READINESS_TEXT[status.readiness];
}

function EngineTitle({ id, label, status }: { readonly id: string; readonly label: string; readonly status: HostEngineStatus | undefined }) {
  return (
    <div className={styles.info}>
      <p className={styles.name}>
        <span id={id}>{label}</span>
        <span className={styles.status}>{statusText(status)}</span>
      </p>
      {status !== undefined && <EngineFacts status={status} />}
    </div>
  );
}

function LoginProgress({ login, onCancel, isCancelling }: { readonly login: HostLogin; readonly onCancel: () => void; readonly isCancelling: boolean }) {
  if (login.state === 'failed') {
    return (
      <p className={styles.problem} data-tone="danger" role="alert">
        {`上一次登入失敗：${login.error ?? '沒有說明原因'}`}
      </p>
    );
  }
  const text = login.state === 'claimed' ? LOGIN_STATE_TEXT.claimed : LOGIN_STATE_TEXT.pending;
  return (
    <div className={styles.addRow}>
      <p className={styles.note} role="status">{`登入進行中：${text}（${formatInstant(login.expiresAt)} 前）`}</p>
      <Button size="sm" aria-disabled={isCancelling || undefined} onClick={onCancel}>
        作廢
      </Button>
    </div>
  );
}

function LogoutDialog({ agent, engine, onClose }: { readonly agent: HostAgent; readonly engine: LocalEngineKind; readonly onClose: () => void }) {
  const logout = useLogoutHostEngine();
  return (
    <ConfirmDialog
      title={`登出主機上的 ${engineLabel(engine)}？`}
      description="這個引擎的來源會立刻停止派工，排隊的工作取消、執行中的停下；主機下次連線時會真的登出。要再用就重新登入。"
      confirmLabel="登出"
      pendingLabel="登出中…"
      tone="danger"
      isPending={logout.isPending}
      keepsFocusWhilePending
      error={logout.isError ? errorMessage(logout.error) : null}
      onConfirm={() => {
        logout.mutate({ agentId: agent.id, engine }, { onSuccess: onClose });
      }}
      onCancel={onClose}
    />
  );
}

function LoginCodeDialog({ agent, engine, started, onClose }: { readonly agent: HostAgent; readonly engine: LocalEngineKind; readonly started: HostLoginStart; readonly onClose: () => void }) {
  return (
    <CodeDialog
      title={`登入主機上的 ${engineLabel(engine)}`}
      description={`「${agent.name}」`}
      code={started.code}
      codeLabel="登入碼"
      expiresAt={started.login.expiresAt}
      command={loginCommand(engine)}
      commandLabel="登入的指令"
      extra={<p className={styles.note}>{loginRule(engine)}</p>}
      onClose={onClose}
    />
  );
}

interface EngineRowProps {
  readonly agent: HostAgent;
  readonly engine: LocalEngineKind;
}

/** 登入碼對話框關掉之後：「開始登入」鈕已經因為登入進行中而不見，焦點交給這一列，不掉到頁面本體。 */
function useFocusRowAfterCode(isShowingCode: boolean) {
  const rowRef = useRef<HTMLLIElement>(null);
  const wasShowing = useRef(false);
  useEffect(() => {
    if (isShowingCode) wasShowing.current = true;
    else if (wasShowing.current) {
      wasShowing.current = false;
      rowRef.current?.focus();
    }
  }, [isShowingCode]);
  return rowRef;
}

/** 開始登入：成功就把一次性的登入碼交給對話框（mutation 不留著它）；失敗的說明留在這一列。 */
function useStartLogin(agent: HostAgent, engine: LocalEngineKind, onBegin: () => void) {
  const start = useStartHostLogin();
  const [started, setStarted] = useState<HostLoginStart | null>(null);
  const run = () => {
    if (start.isPending) return;
    onBegin();
    start.mutate(
      { agentId: agent.id, engine },
      {
        onSuccess: (result) => {
          setStarted(result);
          start.reset();
        },
      },
    );
  };
  return { start, started, clear: () => { setStarted(null); }, run };
}

function EngineActions({ canLogin, isAuthorized, isStarting, label, onLogin, onLogout }: {
  readonly canLogin: boolean;
  readonly isAuthorized: boolean;
  readonly isStarting: boolean;
  readonly label: string;
  readonly onLogin: () => void;
  readonly onLogout: () => void;
}) {
  const loginLabel = isAuthorized ? '重新登入' : '開始登入';
  return (
    <div className={styles.actions}>
      {canLogin && (
        <Button size="sm" aria-label={`${loginLabel} ${label}`} aria-disabled={isStarting || undefined} onClick={onLogin}>
          {loginLabel}
        </Button>
      )}
      {isAuthorized && (
        <Button size="sm" variant="ghost" aria-label={`登出 ${label}`} onClick={onLogout}>
          登出
        </Button>
      )}
    </div>
  );
}

/**
 * 代理上的一個引擎：狀態（文字）、版本、計費方式、誰授權的、問題的說明；開始登入（顯示一次性的登入碼）、登出、作廢進行中的登入。
 * 代理還沒配對或已停用時不能登入。
 */
export function EngineRow({ agent, engine }: EngineRowProps) {
  const status = agent.engines.find((candidate) => candidate.kind === engine);
  const login = currentLogin(agent, engine, status?.authorizedAt ?? null);
  const cancel = useCancelHostLogin();
  // 下一次動作開始時，上一次的失敗說明先收掉。
  const { start, started, clear, run } = useStartLogin(agent, engine, () => { cancel.reset(); });
  const titleId = useId();
  const rowRef = useFocusRowAfterCode(started !== null);
  const [isConfirmingLogout, setIsConfirmingLogout] = useState(false);
  const label = engineLabel(engine);
  // 登出中不給登出、登入：再按一次沒有意義，這時開始的登入也會被接著執行的登出登掉。
  const isLoggingOut = status?.logoutPending === true;
  const isAuthorized = status !== undefined && !isLoggingOut && (status.loggedIn || status.authorizedAt !== null);
  const canLogin = agent.state === 'active' && !isLoggingOut && (login === undefined || login.state === 'failed');
  const problem = status?.problem ?? '';
  const error = [start, cancel].find((mutation) => mutation.isError)?.error;

  return (
    <li ref={rowRef} className={styles.engine} aria-labelledby={titleId} tabIndex={-1}>
      <div className={styles.rowMain}>
        <EngineTitle id={titleId} label={label} status={status} />
        <EngineActions
          canLogin={canLogin}
          isAuthorized={isAuthorized}
          isStarting={start.isPending}
          label={label}
          onLogin={run}
          onLogout={() => { setIsConfirmingLogout(true); }}
        />
      </div>
      {problem !== '' && (
        <p className={styles.problem}>
          <TextWithCommands text={problem} />
        </p>
      )}
      {login !== undefined && (
        <LoginProgress login={login} isCancelling={cancel.isPending} onCancel={() => {
            if (cancel.isPending) return;
            start.reset();
            cancel.mutate(login.id);
          }} />
      )}
      {error !== undefined && <Notice tone="danger">{errorMessage(error)}</Notice>}
      {started !== null && <LoginCodeDialog agent={agent} engine={engine} started={started} onClose={clear} />}
      {isConfirmingLogout && <LogoutDialog agent={agent} engine={engine} onClose={() => { setIsConfirmingLogout(false); }} />}
    </li>
  );
}
