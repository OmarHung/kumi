import { useId, useRef, useState } from 'react';
import { Link } from 'react-router';
import { useApproveCliLogin, type CliLoginCheck, type CliLoginRequest } from '../../api/cliLogin';
import { errorMessage, isApiError } from '../../api/http';
import type { AccessTokenScope } from '../../api/types';
import { Button, buttonClassName } from '../../components/Button/Button';
import { SelectField } from '../../components/Field/Field';
import { Notice } from '../../components/Notice/Notice';
import { navigateTo } from '../../lib/navigateTo';
import { DEFAULT_EXPIRY, EXPIRY_CHOICES, expiryDays } from '../account/accessTokenText';
import { ConsentActions, ConsentHint } from '../oauth/ConsentShell';
import { ScopeChecklist } from '../oauth/ScopeChecklist';
import {
  AGENT_BLOCKED_NOTE,
  AGENT_SKIPPED_NOTE,
  APPROVED_NOTE,
  BAD_REDIRECT_NOTE,
  CLI_SCOPE_OPTIONS,
  DENIED_NOTE,
  denyRedirectUrl,
  isExpectedRedirect,
  orderedScopes,
  preselectedScopes,
  scopeLabel,
} from './cliLoginText';
import styles from './CliAuthorize.module.css';

const EXPIRY_OPTIONS = EXPIRY_CHOICES.map(({ value, label }) => ({ value, label }));

type Outcome = 'approved' | 'denied' | null;

const LIMIT_REACHED = 'access_token.limit_reached';
const TOKENS_PATH = '/account/integrations#tokens';

interface FormProps {
  readonly check: CliLoginCheck;
  readonly request: CliLoginRequest;
}

/**
 * 允許與拒絕：允許成功之後整頁導到 kumi 的迴路（先確認伺服器給的網址真的是那裡）；拒絕不呼叫伺服器，自己導回去。
 * 防重送用 ref 同步鎖住（同一個 task 連按幾次，React 還沒重畫時後面的都擋下來），失敗才放開。
 */
function useDecision(request: CliLoginRequest) {
  const [outcome, setOutcome] = useState<Outcome>(null);
  const [redirectProblem, setRedirectProblem] = useState(false);
  const isLocked = useRef(false);
  const approve = useApproveCliLogin();
  return {
    outcome,
    failed: approve.isError ? approve.error : null,
    redirectProblem,
    isPending: approve.isPending,
    isBusy: approve.isPending || outcome !== null,
    approve: (grantedScopes: readonly AccessTokenScope[], expiresIn: number | null) => {
      if (isLocked.current) return;
      isLocked.current = true;
      setRedirectProblem(false);
      approve.reset();
      approve.mutate(
        { ...request, grantedScopes, expiresInDays: expiresIn },
        {
          onSuccess: ({ redirectTo }) => {
            if (!isExpectedRedirect(redirectTo, request.port)) {
              isLocked.current = false;
              setRedirectProblem(true);
              return;
            }
            setOutcome('approved');
            navigateTo(redirectTo);
          },
          onError: () => {
            isLocked.current = false;
          },
        },
      );
    },
    deny: () => {
      if (isLocked.current) return;
      isLocked.current = true;
      setOutcome('denied');
      navigateTo(denyRedirectUrl(request.port, request.state));
    },
  };
}

/** 失敗的提示：固定存在的 alert（沒有失敗時看不見、不佔位）。額度滿了給「到存取權杖撤銷不用的」（另開分頁，這一頁的請求還在）。 */
function FailureNotice({ failed, hasRedirectProblem }: { readonly failed: unknown; readonly hasRedirectProblem: boolean }) {
  const isLimit = isApiError(failed) && failed.code === LIMIT_REACHED;
  return (
    <Notice
      tone="danger"
      role="alert"
      actions={
        isLimit ? (
          <Link className={buttonClassName('secondary', 'sm')} to={TOKENS_PATH} target="_blank" rel="noreferrer">
            到存取權杖撤銷不用的
          </Link>
        ) : undefined
      }
    >
      {hasRedirectProblem ? BAD_REDIRECT_NOTE : failed === null ? null : errorMessage(failed)}
    </Notice>
  );
}

interface AgentBlockProps {
  readonly check: CliLoginCheck;
  readonly wantsCli: boolean;
  /** 只要本機代理又不允許時，「允許」的停用原因就是這一塊的說明：按鈕的 aria-describedby 接到它。 */
  readonly noteId: string;
}

function AgentBlock({ check, wantsCli, noteId }: AgentBlockProps) {
  return (
    <div className={styles.section}>
      <h2 className={styles.sectionTitle}>本機代理</h2>
      {check.agentAllowed ? (
        <>
          <p className={styles.note}>會另外建立一把本機代理用的權杖，範圍固定，只能：</p>
          <ul className={styles.list}>
            {orderedScopes(check.agentScopes).map((scope) => (
              <li key={scope}>{scopeLabel(scope)}</li>
            ))}
          </ul>
        </>
      ) : (
        <p id={noteId} className={styles.note}>
          {wantsCli ? AGENT_SKIPPED_NOTE : AGENT_BLOCKED_NOTE}
        </p>
      )}
    </div>
  );
}

function ApprovedStatus({ outcome }: { readonly outcome: Outcome }) {
  return (
    <Notice tone="success" role="status">
      {outcome === 'approved' ? APPROVED_NOTE : outcome === 'denied' ? DENIED_NOTE : null}
    </Notice>
  );
}

/** 「允許」停用的一行說明（在永遠掛著的提示裡）；只要本機代理又不允許的原因在那一塊自己的說明裡，不重複寫。 */
function blockingHint(check: CliLoginCheck, selected: readonly AccessTokenScope[]): string | null {
  return check.tokens.includes('cli') && selected.length === 0 ? '至少要勾一項' : null;
}

/** 只要本機代理、組織又不允許：沒有任何權杖可建。 */
function isAgentOnlyBlocked(check: CliLoginCheck): boolean {
  return !check.tokens.includes('cli') && check.tokens.includes('agent') && !check.agentAllowed;
}

interface FieldsProps {
  readonly check: CliLoginCheck;
  readonly selected: readonly AccessTokenScope[];
  readonly onSelect: (scopes: readonly AccessTokenScope[]) => void;
  readonly expiry: string;
  readonly onExpiry: (value: string) => void;
  readonly isBusy: boolean;
  readonly noteId: string;
}

/** 要給的範圍與期限：命令列的勾選、本機代理的固定範圍、期限。 */
function ConsentFields({ check, selected, onSelect, expiry, onExpiry, isBusy, noteId }: FieldsProps) {
  const wantsCli = check.tokens.includes('cli');
  return (
    <>
      {wantsCli && (
        <ScopeChecklist legend="命令列可以做這些事" options={CLI_SCOPE_OPTIONS} selected={selected} disabled={isBusy} onChange={onSelect} />
      )}
      {check.tokens.includes('agent') && <AgentBlock check={check} wantsCli={wantsCli} noteId={noteId} />}
      <SelectField label="期限" value={expiry} options={EXPIRY_OPTIONS} disabled={isBusy} onChange={onExpiry} />
    </>
  );
}

/**
 * 命令列的範圍與期限、本機代理的說明，以及「允許」「拒絕」（internal/HANDOFF-setup-wizard.md 第 5.1 節）。
 * 「允許」停用的原因（一個都沒勾、只要本機代理又不允許）寫在永遠掛著的一行說明裡，接到按鈕的 aria-describedby；
 * 停用用 aria-disabled（不是原生 disabled），焦點留得住。結果的提示是兩個固定存在的 live region，放在按鈕後面，出現時不推動按鈕。
 */
export function CliAuthorizeForm({ check, request }: FormProps) {
  const wantsCli = check.tokens.includes('cli');
  const [selected, setSelected] = useState<readonly AccessTokenScope[]>(() => preselectedScopes(check.scopes));
  const [expiry, setExpiry] = useState(DEFAULT_EXPIRY);
  const hintId = useId();
  const agentNoteId = useId();
  const decision = useDecision(request);

  const hint = blockingHint(check, selected);
  const isAgentBlocked = isAgentOnlyBlocked(check);
  const isBlocked = hint !== null || isAgentBlocked;
  const describedBy = isAgentBlocked ? `${hintId} ${agentNoteId}` : hintId;

  return (
    <>
      <ConsentFields
        check={check}
        selected={selected}
        onSelect={setSelected}
        expiry={expiry}
        onExpiry={setExpiry}
        isBusy={decision.isBusy}
        noteId={agentNoteId}
      />
      <ConsentHint id={hintId}>{hint}</ConsentHint>
      <ConsentActions>
        <Button
          variant="primary"
          aria-disabled={isBlocked || decision.isBusy || undefined}
          aria-describedby={describedBy}
          onClick={() => {
            if (isBlocked || decision.isBusy) return;
            decision.approve(wantsCli ? selected : [], expiryDays(expiry));
          }}
        >
          {decision.isPending ? '授權中…' : '允許'}
        </Button>
        <Button disabled={decision.isBusy} onClick={decision.deny}>
          拒絕
        </Button>
      </ConsentActions>
      <FailureNotice failed={decision.failed} hasRedirectProblem={decision.redirectProblem} />
      <ApprovedStatus outcome={decision.outcome} />
    </>
  );
}
