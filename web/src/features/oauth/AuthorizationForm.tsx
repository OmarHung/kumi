import { useId, useRef, useState } from 'react';
import { redirectTargetOf, useApproveAuthorization, useDenyAuthorization, type AuthorizationCheck, type AuthorizationRequest } from '../../api/oauth';
import { errorMessage } from '../../api/http';
import type { AccessTokenScope } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { isNavigableUrl } from '../../lib/navigableUrl';
import { navigateTo } from '../../lib/navigateTo';
import { ConsentActions, ConsentFacts, ConsentHint } from './ConsentShell';
import { consentScopeLabel, defaultScopes, orderedScopes } from './oauthText';
import { ScopeChecklist } from './ScopeChecklist';

type Outcome = 'approved' | 'denied' | null;

const OUTCOME_TEXT: Readonly<Record<'approved' | 'denied', (clientName: string) => string>> = {
  approved: (clientName) => `已經授權，可以回到 ${clientName} 了。`,
  denied: (clientName) => `已經拒絕，可以回到 ${clientName} 了。`,
};

interface AuthorizationFormProps {
  readonly check: AuthorizationCheck;
  readonly request: AuthorizationRequest;
}

/**
 * 允許與拒絕：成功之後整頁導到伺服器給的網址；失敗時留著，錯誤與導回的網址（有的話）給畫面。
 * 防重送用 ref 同步鎖住：同一個 task 連按幾次，React 還沒重畫（按鈕還沒停用）時後面的都擋下來；失敗才放開。
 */
function useDecision(request: AuthorizationRequest) {
  const [outcome, setOutcome] = useState<Outcome>(null);
  const isLocked = useRef(false);
  const approve = useApproveAuthorization(request);
  const deny = useDenyAuthorization(request);
  const failed = approve.isError ? approve.error : deny.isError ? deny.error : null;

  function finish(result: Outcome, redirectTo: string) {
    setOutcome(result);
    if (isNavigableUrl(redirectTo)) navigateTo(redirectTo);
  }
  function begin(): boolean {
    if (isLocked.current) return false;
    isLocked.current = true;
    approve.reset();
    deny.reset();
    return true;
  }
  function unlock() {
    isLocked.current = false;
  }
  return {
    outcome,
    failed,
    isPending: approve.isPending,
    isBusy: approve.isPending || deny.isPending || outcome !== null,
    approve: (scopes: readonly AccessTokenScope[]) => {
      if (!begin()) return;
      approve.mutate(scopes, { onSuccess: (result) => { finish('approved', result.redirectTo); }, onError: unlock });
    },
    deny: () => {
      if (!begin()) return;
      deny.mutate(undefined, { onSuccess: (result) => { finish('denied', result.redirectTo); }, onError: unlock });
    },
  };
}

/** 失敗的提示：固定存在的 alert（沒有失敗時看不見、不佔位）。伺服器給了導回的網址就多一個「回到應用」。 */
function FailureNotice({ failed }: { readonly failed: unknown }) {
  const redirectTo = redirectTargetOf(failed);
  return (
    <Notice
      tone="danger"
      role="alert"
      actions={
        failed !== null && redirectTo !== null ? (
          <Button size="sm" onClick={() => { navigateTo(redirectTo); }}>
            回到應用
          </Button>
        ) : undefined
      }
    >
      {failed === null ? null : errorMessage(failed)}
    </Notice>
  );
}

/**
 * 範圍的勾選與「允許」「拒絕」。有驗證的應用請求的範圍預設全勾；沒驗證的只預設勾讀取的，寫入的要自己勾。
 * 一個都沒勾時「允許」停用（至少要留一個），原因寫在永遠掛著的一行說明裡（接到按鈕的 aria-describedby）。
 * 結果的提示（失敗的 alert、成功的 status）是兩個固定存在的 live region，放在按鈕後面：出現時不推動按鈕，
 * 頁面也從上方開始排，卡片長高不會把按鈕往上帶。
 * 成功之後導到本機的網址時瀏覽器可能停在這一頁，所以同時換成「可以回到應用了」。
 */
export function AuthorizationForm({ check, request }: AuthorizationFormProps) {
  const options = orderedScopes(check.requestedScopes);
  const [selected, setSelected] = useState<readonly AccessTokenScope[]>(() => defaultScopes(options, check.clientHost !== null));
  const hintId = useId();
  const decision = useDecision(request);

  return (
    <>
      <ScopeChecklist
        legend="允許它做這些事"
        options={options.map((scope) => ({ scope, label: consentScopeLabel(scope) }))}
        selected={selected}
        disabled={decision.isBusy}
        onChange={setSelected}
      />
      <ConsentHint id={hintId}>{selected.length === 0 ? '至少要勾一項' : null}</ConsentHint>
      <ConsentFacts>
        授權之後會回到 <strong>{check.redirectHost}</strong>
      </ConsentFacts>
      {check.redirectIsLoopback && <ConsentFacts>這是你電腦上的程式。只在你自己電腦上的程式要求時才允許。</ConsentFacts>}
      <ConsentActions>
        <Button
          variant="primary"
          disabled={decision.isBusy || selected.length === 0}
          aria-describedby={hintId}
          onClick={() => {
            decision.approve(orderedScopes(selected));
          }}
        >
          {decision.isPending ? '授權中…' : '允許'}
        </Button>
        <Button disabled={decision.isBusy} onClick={decision.deny}>
          拒絕
        </Button>
      </ConsentActions>
      <FailureNotice failed={decision.failed} />
      <Notice tone="success" role="status">
        {decision.outcome === null ? null : OUTCOME_TEXT[decision.outcome](check.clientName)}
      </Notice>
    </>
  );
}
