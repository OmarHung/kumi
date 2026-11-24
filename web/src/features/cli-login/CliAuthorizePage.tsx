import { useId, useMemo } from 'react';
import { useSearchParams } from 'react-router';
import { useCliLoginCheck, type CliLoginCheck } from '../../api/cliLogin';
import { errorMessage } from '../../api/http';
import { Notice } from '../../components/Notice/Notice';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import { COMMAND_NAME } from '../../config/product';
import { ConsentFacts, ConsentHeading, ConsentShell, NoWrap } from '../oauth/ConsentShell';
import { CliAuthorizeForm } from './CliAuthorizeForm';
import { WARNING_NOTE, requestFromSearch } from './cliLoginText';

const RETRY_HINT = `請回到終端機重新執行 ${COMMAND_NAME} auth login。`;

function Problem({ message }: { readonly message: string }) {
  return (
    <Notice tone="danger" role="alert">
      這個登入請求有問題：{message}
      {RETRY_HINT}
    </Notice>
  );
}

function Who({ check }: { readonly check: CliLoginCheck }) {
  const { account } = check;
  return (
    <>
      <ConsentFacts>
        電腦：<strong>{check.machineName}</strong>（名稱是那台電腦上的 {COMMAND_NAME} 自己報的）
      </ConsentFacts>
      <ConsentFacts>
        你目前登入的是 <strong>{account.displayName}</strong>（{account.email}）· {account.organizationName}
      </ConsentFacts>
      <Notice tone="warning" role="none">
        {WARNING_NOTE}
      </Notice>
    </>
  );
}

/**
 * kumi 的瀏覽器登入同意頁（internal/HANDOFF-setup-wizard.md 第 5.1 節）：終端機裡的 kumi 把使用者帶來，
 * 在這裡確認「是不是我剛剛執行的」，按「允許」之後伺服器發權杖的碼，瀏覽器被導回那台電腦上 kumi 開的迴路。
 * 和 OAuth 的同意頁一樣在 RequireAuth 底下、AppShell 外面；沒登入時先到登入頁，登入完帶著整串查詢字串回來。
 */
export function CliAuthorizePage() {
  const [params] = useSearchParams();
  const headingId = useId();
  const parsed = useMemo(() => requestFromSearch(params), [params]);
  const request = 'request' in parsed ? parsed.request : null;
  const check = useCliLoginCheck(request, params.toString());
  const isPending = request !== null && check.isPending;

  return (
    <ConsentShell documentTitle="登入" isPending={isPending} headingId={headingId}>
      {'problem' in parsed && (
        <>
          <ConsentHeading id={headingId}>無法登入</ConsentHeading>
          <Problem message={parsed.problem} />
        </>
      )}
      {isPending && <LoadingState label="正在確認登入請求…" />}
      {check.isError && (
        <>
          <ConsentHeading id={headingId}>無法登入</ConsentHeading>
          <Problem message={errorMessage(check.error)} />
        </>
      )}
      {request !== null && check.isSuccess && (
        <>
          <ConsentHeading id={headingId}>
            讓這台電腦上的 {COMMAND_NAME} 使用你的<NoWrap>帳號？</NoWrap>
          </ConsentHeading>
          <Who check={check.data} />
          <CliAuthorizeForm check={check.data} request={request} />
        </>
      )}
    </ConsentShell>
  );
}
