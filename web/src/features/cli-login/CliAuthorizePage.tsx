import { useId, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useCliLoginCheck, type CliLoginCheck } from '../../api/cliLogin';
import { errorMessage } from '../../api/http';
import { Button, buttonClassName } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import { COMMAND_NAME } from '../../config/product';
import { navigateTo } from '../../lib/navigateTo';
import { ConsentActions, ConsentFacts, ConsentHeading, ConsentShell, NoWrap } from '../oauth/ConsentShell';
import { CliAuthorizeForm } from './CliAuthorizeForm';
import { DENIED_NOTE, OUTDATED_NOTE, OUTDATED_TITLE, WARNING_NOTE, denyRedirectUrl, requestFromSearch } from './cliLoginText';

const INTEGRATIONS_PATH = '/account/integrations';

const RETRY_HINT = `請回到終端機重新執行 ${COMMAND_NAME} auth login。`;

function Problem({ message }: { readonly message: string }) {
  return (
    <Notice tone="danger" role="alert">
      這個登入請求有問題：{message}
      {RETRY_HINT}
    </Notice>
  );
}

/** 確認碼欄位之前只放「你是誰」與警告：對方的 kumi 自己報的資訊（電腦名稱）放在按鈕之後，不要讓人把它當成確認碼。 */
function Who({ check }: { readonly check: CliLoginCheck }) {
  const { account } = check;
  return (
    <>
      <ConsentFacts>
        你目前登入的是 <strong>{account.displayName}</strong>（{account.email}）· {account.organizationName}
      </ConsentFacts>
      <Notice tone="warning" role="none">
        {WARNING_NOTE}
      </Notice>
    </>
  );
}

function ReportedMachine({ check }: { readonly check: CliLoginCheck }) {
  return (
    <ConsentFacts>
      電腦：{check.machineName}（名稱是那台電腦上的 {COMMAND_NAME} 自己報的，不是確認碼）
    </ConsentFacts>
  );
}

/**
 * 網址沒有 confirm=1：這個 kumi 不會顯示確認碼，而伺服器一律要求確認碼，所以這裡不畫表單、不問伺服器，
 * 只說明要更新，並給「拒絕」（不是自己剛執行的登入就該拒絕）。伺服器不辨認 kumi 的版本：網址是誰組的都可能，
 * confirm 只決定畫面。
 */
function OutdatedCli({ port, state, headingId }: { readonly port: number; readonly state: string; readonly headingId: string }) {
  const [isDenied, setIsDenied] = useState(false);
  return (
    <>
      <ConsentHeading id={headingId}>{OUTDATED_TITLE}</ConsentHeading>
      <ConsentFacts>{OUTDATED_NOTE}</ConsentFacts>
      <ConsentActions>
        <Link className={buttonClassName('secondary', 'md')} to={INTEGRATIONS_PATH}>
          前往「命令列與整合」
        </Link>
        <Button
          disabled={isDenied}
          onClick={() => {
            setIsDenied(true);
            navigateTo(denyRedirectUrl(port, state));
          }}
        >
          拒絕
        </Button>
      </ConsentActions>
      <Notice tone="success" role="status">
        {isDenied ? DENIED_NOTE : null}
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
  const isOutdated = 'request' in parsed && !parsed.showsConfirmation;
  // 太舊的 kumi 不問伺服器：沒有東西可以允許。
  const check = useCliLoginCheck(isOutdated ? null : request, params.toString());
  const isPending = request !== null && !isOutdated && check.isPending;

  return (
    <ConsentShell documentTitle="登入" isPending={isPending} headingId={headingId}>
      {'problem' in parsed && (
        <>
          <ConsentHeading id={headingId}>無法登入</ConsentHeading>
          <Problem message={parsed.problem} />
        </>
      )}
      {request !== null && isOutdated && <OutdatedCli port={request.port} state={request.state} headingId={headingId} />}
      {isPending && <LoadingState label="正在確認登入請求…" />}
      {check.isError && (
        <>
          <ConsentHeading id={headingId}>無法登入</ConsentHeading>
          <Problem message={errorMessage(check.error)} />
        </>
      )}
      {request !== null && !isOutdated && check.isSuccess && (
        <>
          <ConsentHeading id={headingId}>
            讓這台電腦上的 {COMMAND_NAME} 使用你的<NoWrap>帳號？</NoWrap>
          </ConsentHeading>
          <Who check={check.data} />
          <CliAuthorizeForm check={check.data} request={request} />
          <ReportedMachine check={check.data} />
        </>
      )}
    </ConsentShell>
  );
}
