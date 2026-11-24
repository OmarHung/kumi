import { useId } from 'react';
import { useSearchParams } from 'react-router';
import { redirectTargetOf, useAuthorizationCheck, type AuthorizationCheck } from '../../api/oauth';
import { errorMessage } from '../../api/http';
import { Badge } from '../../components/Badge/Badge';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import { PRODUCT_NAME } from '../../config/product';
import { navigateTo } from '../../lib/navigateTo';
import { AuthorizationForm } from './AuthorizationForm';
import { ConsentFacts, ConsentHeading, ConsentShell, NoWrap } from './ConsentShell';
import { requestFromSearch } from './oauthText';
import styles from './OAuthConsentPage.module.css';

function Problem({ error }: { readonly error: unknown }) {
  const redirectTo = redirectTargetOf(error);
  return (
    <Notice
      tone="danger"
      role="alert"
      actions={
        redirectTo === null ? undefined : (
          <Button
            size="sm"
            onClick={() => {
              navigateTo(redirectTo);
            }}
          >
            回到應用
          </Button>
        )
      }
    >
      這個授權請求有問題：{errorMessage(error)}
    </Notice>
  );
}

const PUNYCODE_PREFIX = 'xn--';

/** 後端把網域轉成 punycode 顯示：含 xn-- 的標籤表示原本有非英文字元（長得像別的網站的字），提醒使用者確認。 */
function isPunycodeHost(host: string): boolean {
  return host.split('.').some((label) => label.toLowerCase().startsWith(PUNYCODE_PREFIX));
}

/**
 * 誰要用你的帳號。網域比名稱可信（名稱是應用自己填的），所以「由 {網域} 提供」緊接在標題下面、用比內文大的字；
 * 沒驗證的應用沒有網域可信，標題改說法、名稱旁標「未驗證」，並警告。
 */
function Who({ check }: { readonly check: AuthorizationCheck }) {
  const { account, clientHost } = check;
  return (
    <>
      {clientHost === null ? (
        <>
          <p className={styles.provider}>
            <Badge tone="warning">未驗證</Badge> {check.clientName}
          </p>
          <Notice tone="warning" role="none">
            這個應用沒有經過驗證。只有在你自己剛剛從這個應用發起連線時才允許。
          </Notice>
        </>
      ) : (
        <>
          <p className={styles.provider}>由 {clientHost} 提供</p>
          {isPunycodeHost(clientHost) && <ConsentFacts>網域含有非英文字元，請確認是你認得的網站</ConsentFacts>}
        </>
      )}
      <ConsentFacts>
        你目前登入的是 <strong>{account.displayName}</strong>（{account.email}）· {account.organizationName}
      </ConsentFacts>
    </>
  );
}

/**
 * OAuth 的同意頁（internal/HANDOFF-mcp.md 第 6.1 節）：用戶端（例如使用者電腦上的 Claude Code）把使用者帶來，
 * 在這裡確認「誰要用你的帳號、能做什麼、授權完回到哪裡」。沒有側欄，也不在 AppShell 底下：
 * 即時更新、新通知的提示、離開確認都是 AppShell／各編輯頁掛的，這一頁不會被它們打擾。
 * 網址列的參數原樣交給伺服器檢查；有問題而且不能導回時只顯示錯誤，能導回時另給「回到應用」。
 */
export function OAuthConsentPage() {
  const [params] = useSearchParams();
  const headingId = useId();
  const check = useAuthorizationCheck(requestFromSearch(params), params.toString());

  return (
    <ConsentShell documentTitle="授權" isPending={check.isPending} headingId={headingId}>
      {check.isPending && <LoadingState label="正在確認授權請求…" />}
      {check.isError && (
        <>
          <ConsentHeading id={headingId}>無法授權</ConsentHeading>
          <Problem error={check.error} />
        </>
      )}
      {check.isSuccess && (
        <>
          <ConsentHeading id={headingId}>
            {check.data.clientHost === null ? `一個未經驗證的應用「${check.data.clientName}」要求使用你的 ` : `授權 ${check.data.clientName} 使用你的 `}
            <NoWrap>{PRODUCT_NAME}</NoWrap> 帳號
          </ConsentHeading>
          <Who check={check.data} />
          <AuthorizationForm check={check.data} request={requestFromSearch(params)} />
        </>
      )}
    </ConsentShell>
  );
}
