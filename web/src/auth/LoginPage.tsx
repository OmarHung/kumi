import { useId, useState, type SubmitEvent } from 'react';
import { Navigate, useLocation, useSearchParams } from 'react-router';
import { errorMessage } from '../api/http';
import type { SignInRequest } from '../api/types';
import { Button } from '../components/Button/Button';
import { DocumentTitle } from '../components/DocumentTitle';
import { TextField } from '../components/Field/Field';
import { FormAlert } from '../components/FormAlert/FormAlert';
import { Notice } from '../components/Notice/Notice';
import { LoadingState } from '../components/StatusViews/StatusViews';
import { PRODUCT_NAME } from '../config/product';
import { isSessionExpiredState, nextPathFrom } from './nextPath';
import { useSession, useSignIn } from './session';
import styles from './LoginPage.module.css';

function textValue(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value : '';
}

interface LoginFormProps {
  /** 驗證或登入失敗的訊息；兩個欄位都用 aria-describedby 指向它。 */
  readonly message: string | null;
  readonly isPending: boolean;
  readonly onSubmit: (request: SignInRequest) => void;
}

function LoginForm({ message, isPending, onSubmit }: LoginFormProps) {
  const errorId = useId();
  const describedBy = message === null ? undefined : errorId;
  const isInvalid = message !== null || undefined;

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    onSubmit({ email: textValue(form, 'email').trim(), password: textValue(form, 'password') });
  }

  return (
    <form className={styles.form} noValidate onSubmit={handleSubmit}>
      <FormAlert id={errorId} message={message} />
      <TextField
        label="Email"
        name="email"
        type="email"
        autoComplete="username"
        inputMode="email"
        aria-describedby={describedBy}
        aria-invalid={isInvalid}
      />
      <TextField
        label="密碼"
        name="password"
        type="password"
        autoComplete="current-password"
        aria-describedby={describedBy}
        aria-invalid={isInvalid}
      />
      <Button type="submit" variant="primary" className={styles.submit} disabled={isPending}>
        {isPending ? '登入中…' : '登入'}
      </Button>
    </form>
  );
}

export function LoginPage() {
  const session = useSession();
  const signIn = useSignIn();
  const [searchParams] = useSearchParams();
  const isSessionExpired = isSessionExpiredState(useLocation().state);
  const [validationError, setValidationError] = useState<string | null>(null);
  const headingId = useId();

  if (session.data) {
    return <Navigate to={nextPathFrom(searchParams)} replace />;
  }
  // 確認中也是同一張桌面（沒有外框），載入的文字放在畫面中間，不會貼在左上角。
  if (session.isPending) {
    return (
      <main className={styles.page}>
        <LoadingState label="正在確認登入狀態…" />
      </main>
    );
  }

  function handleSubmit(request: SignInRequest) {
    const isMissing = request.email === '' || request.password === '';
    setValidationError(isMissing ? '請輸入 Email 與密碼。' : null);
    if (!isMissing) {
      signIn.mutate(request);
    }
  }

  const message = validationError ?? (signIn.isError ? errorMessage(signIn.error) : null);
  // 沒有外框的獨立頁面：桌面（--color-canvas）上放一張紙（docs/DESIGN.md 6.1）。
  return (
    <main className={styles.page}>
      <DocumentTitle title="登入" />
      <section className={styles.panel} aria-labelledby={headingId}>
        <p className={styles.brand}>{PRODUCT_NAME}</p>
        <h1 id={headingId} className={styles.heading}>
          登入
        </h1>
        {/* 已經登入、之後才收到 401 而回到這裡（RequireAuth 帶的 state）：說明為什麼要重新登入。第一次打開、自己登出時沒有。 */}
        {isSessionExpired && (
          <Notice tone="info" className={styles.expired}>
            登入已經失效，請重新登入。
          </Notice>
        )}
        <LoginForm message={message} isPending={signIn.isPending} onSubmit={handleSubmit} />
      </section>
    </main>
  );
}
