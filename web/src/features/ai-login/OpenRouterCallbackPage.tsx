import { useEffect, useId, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import type { AiConnectionScope } from '../../api/aiTypes';
import { errorMessage, isApiError } from '../../api/http';
import { useCompleteOpenRouterLogin } from '../../api/personalAi';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { buttonClassName } from '../../components/Button/Button';
import { DocumentTitle } from '../../components/DocumentTitle';
import { Notice } from '../../components/Notice/Notice';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import { PRODUCT_NAME } from '../../config/product';
import styles from './AiLogin.module.css';

const HTTP_TOO_MANY_REQUESTS = 429;
const TITLE = 'OpenRouter 登入';
const CALLBACK_PATH = '/ai/openrouter/callback';

/** 成功之後去哪：組織的連線到 AI 設定頁、自己的到「我的 AI 助理」頁，帶著編號讓那一頁直接打開編輯對話框。 */
export function afterLoginPath(scope: AiConnectionScope, connectionId: string): string {
  const id = encodeURIComponent(connectionId);
  return scope === 'organization' ? `/ai-settings?edit=${id}` : `/account/ai?aiConnection=${id}`;
}

function failureText(error: unknown): string {
  return isApiError(error) && error.status === HTTP_TOO_MANY_REQUESTS ? '請求太頻繁，請稍後再試。' : errorMessage(error);
}

/** 失敗時回去的地方：管理員兩邊都可能是從那裡來的，成員只有「我的 AI 助理」頁。 */
function BackLinks() {
  const user = useCurrentUser();
  return (
    <div className={styles.actions}>
      {isOrganizationAdmin(user) && (
        <Link to="/ai-settings" className={buttonClassName('secondary')}>
          回到 AI 助理設定
        </Link>
      )}
      <Link to="/account/ai" className={buttonClassName('secondary')}>
        回到我的 AI 助理
      </Link>
    </div>
  );
}

function Failure({ headingId, title, children }: { readonly headingId: string; readonly title: string; readonly children: string }) {
  return (
    <>
      <h1 id={headingId} className={styles.heading}>
        {title}
      </h1>
      <Notice tone="danger" role="alert">
        {children}
      </Notice>
      <BackLinks />
    </>
  );
}

/**
 * 登入完成：OpenRouter 帶著 code 與 state 回到這裡，把它們交給伺服器換成金鑰、建好連線，再導到設定的那一頁。
 * 沒有 code（OpenRouter 拒絕或使用者取消）不送請求。code 只能用一次：StrictMode 的第二次掛上、
 * 或重新渲染，都不能再送一次，所以用 ref 記著已經送過。成功後用 replace 導走，上一頁不會又回到這個網址。
 * 這一頁不在 AppShell 底下：即時更新、新通知的提示、離開確認都不會打擾。
 */
export function OpenRouterCallbackPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const headingId = useId();
  const complete = useCompleteOpenRouterLogin();
  const { mutate: completeLogin } = complete;
  const hasStarted = useRef(false);
  // 開頭記下網址上的 code 與 state：送出後會把它們從網址拿掉（不留在瀏覽紀錄、重新整理不會再送一次）。
  const [{ code, state }] = useState(() => ({ code: params.get('code'), state: params.get('state') }));
  const isDenied = code === null || code === '' || state === null || state === '';

  useEffect(() => {
    if (isDenied || hasStarted.current) return;
    hasStarted.current = true;
    void navigate(CALLBACK_PATH, { replace: true });
    completeLogin(
      { code, state },
      {
        onSuccess: (result) => {
          void navigate(afterLoginPath(result.scope, result.connection.id), { replace: true });
        },
      },
    );
  }, [isDenied, code, state, navigate, completeLogin]);

  return (
    <main className={styles.page}>
      <DocumentTitle title={TITLE} />
      <section className={styles.panel} aria-labelledby={isDenied || complete.isError ? headingId : undefined} aria-label={isDenied || complete.isError ? undefined : TITLE}>
        <p className={styles.brand}>{PRODUCT_NAME}</p>
        {isDenied && (
          <Failure headingId={headingId} title="沒有完成 OpenRouter 的登入">
            OpenRouter 沒有帶回登入的結果，可能是你取消了，或這個網址不是從 OpenRouter 回來的。可以回去再試一次。
          </Failure>
        )}
        {!isDenied && !complete.isError && <LoadingState label="正在完成 OpenRouter 的登入…" />}
        {!isDenied && complete.isError && (
          <Failure headingId={headingId} title="無法完成登入">
            {failureText(complete.error)}
          </Failure>
        )}
      </section>
    </main>
  );
}
