import { http } from 'msw';
import type { EmailNotificationPreference, EmailSettings, EmailSettingsInput } from '../api/emailSettings';
import { failure, ok } from './api';
import { server } from './server';

export const EMPTY_EMAIL_SETTINGS: EmailSettings = {
  enabled: false,
  provider: 'gmail',
  host: null,
  port: null,
  security: null,
  username: null,
  hasPassword: false,
  fromAddress: null,
  fromName: null,
  siteUrl: null,
  status: { lastSentAt: null, lastFailedAt: null, lastError: null },
  updatedAt: null,
  updatedBy: null,
};

interface Rejection {
  readonly status: number;
  readonly code: string;
  readonly message: string;
  /** 422 時的細項：路徑是欄位名稱（camelCase）。 */
  readonly details?: readonly { readonly code: string; readonly path: string; readonly message: string }[];
}

export interface EmailSettingsServer {
  /** PUT 收到的本文（照順序）。 */
  readonly saved: EmailSettingsInput[];
  /** POST /test 收到的本文。 */
  readonly tests: EmailSettingsInput[];
  readonly current: () => EmailSettings;
  /** 下一次儲存回這個錯誤（只一次）。 */
  readonly rejectNextSave: (error: Rejection) => void;
  /** 下一次寄測試信回這個錯誤（只一次）。 */
  readonly rejectNextTest: (error: Rejection) => void;
}

function rejected({ status, code, message, details = [] }: Rejection) {
  return failure(status, code, message, details);
}

function applied(current: EmailSettings, input: EmailSettingsInput): EmailSettings {
  const password = input.password;
  return {
    ...current,
    enabled: input.enabled,
    provider: input.provider,
    host: input.host,
    port: input.port,
    security: input.security,
    username: input.username,
    // null 沿用、空字串清掉、其他是新的密碼。
    hasPassword: password === null ? current.hasPassword : password !== '',
    fromAddress: input.fromAddress,
    fromName: input.fromName,
    siteUrl: input.siteUrl,
    updatedAt: '2026-11-21T06:00:00+00:00',
    updatedBy: { personId: 'p-owner', displayName: '範例擁有者' },
  };
}

/**
 * 後端的規則：密碼 null（沿用）時，寄信服務、主機、連接埠或帳號和已存的不同就擋下來（422，細項路徑 password）：
 * 不能把已存的密碼送給新的對象。
 */
function reusedPasswordRejection(current: EmailSettings, input: EmailSettingsInput): Rejection | null {
  const changed =
    input.provider !== current.provider ||
    input.host !== current.host ||
    input.port !== current.port ||
    input.username !== current.username;
  if (!current.hasPassword || input.password !== null || !changed) return null;
  const message = '改了寄信服務、主機、連接埠或帳號，要重新輸入密碼。';
  return {
    status: 422,
    code: 'email.settings_invalid',
    message,
    details: [{ code: 'email.field_required', path: 'password', message }],
  };
}

/** 有狀態的寄信設定端點（internal/HANDOFF-email-notifications.md 第 6 節）：PUT 之後 GET 讀到新的，密碼不會讀出來。 */
export function serveEmailSettings(initial: Partial<EmailSettings> = {}, sentTo = 'user@example.com'): EmailSettingsServer {
  let current: EmailSettings = { ...EMPTY_EMAIL_SETTINGS, ...initial };
  let saveRejection: Rejection | undefined;
  let testRejection: Rejection | undefined;
  const saved: EmailSettingsInput[] = [];
  const tests: EmailSettingsInput[] = [];
  server.use(
    http.get('/v1/email-settings', () => ok(current)),
    http.put('/v1/email-settings', async ({ request }) => {
      const body = (await request.json()) as EmailSettingsInput;
      saved.push(body);
      const error = saveRejection ?? reusedPasswordRejection(current, body);
      saveRejection = undefined;
      if (error !== null) return rejected(error);
      current = applied(current, body);
      return ok(current);
    }),
    http.post('/v1/email-settings/test', async ({ request }) => {
      const body = (await request.json()) as EmailSettingsInput;
      tests.push(body);
      const error = testRejection ?? reusedPasswordRejection(current, body);
      testRejection = undefined;
      if (error !== null) return rejected(error);
      return ok({ sentTo });
    }),
  );
  return {
    saved,
    tests,
    current: () => current,
    rejectNextSave: (error) => {
      saveRejection = error;
    },
    rejectNextTest: (error) => {
      testRejection = error;
    },
  };
}

/** 不是管理員：讀寫都是 403。 */
export function forbidEmailSettings(): void {
  server.use(
    http.get('/v1/email-settings', () => failure(403, 'email.forbidden', '只有組織的擁有者與管理員可以設定寄信。')),
  );
}

export interface EmailPreferenceServer {
  /** PUT 收到的 enabled（照順序）。 */
  readonly puts: boolean[];
  readonly current: () => EmailNotificationPreference;
  /** 下一次儲存回這個錯誤（只一次）。 */
  readonly rejectNext: (error: Rejection) => void;
}

/** 有狀態的「我的帳號」Email 通知開關。 */
export function serveEmailPreference(initial: EmailNotificationPreference = { enabled: true, organizationEnabled: false }): EmailPreferenceServer {
  let current = initial;
  let rejection: Rejection | undefined;
  const puts: boolean[] = [];
  server.use(
    http.get('/v1/account/email-notifications', () => ok(current)),
    http.put('/v1/account/email-notifications', async ({ request }) => {
      const body = (await request.json()) as { enabled: boolean };
      puts.push(body.enabled);
      if (rejection !== undefined) {
        const error = rejection;
        rejection = undefined;
        return rejected(error);
      }
      current = { ...current, enabled: body.enabled };
      return ok(current);
    }),
  );
  return {
    puts,
    current: () => current,
    rejectNext: (error) => {
      rejection = error;
    },
  };
}
