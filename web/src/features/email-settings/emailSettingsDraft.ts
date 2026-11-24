import type { EmailProvider, EmailSettings, EmailSettingsInput, SmtpSecurity } from '../../api/emailSettings';

/** 表單上的欄位（細項路徑的名稱，camelCase），照畫面上的順序。 */
export type EmailField = 'username' | 'password' | 'host' | 'port' | 'security' | 'fromAddress' | 'fromName' | 'siteUrl';

/** 表單的草稿：全部是畫面上的樣子（連接埠是選單的值、沒填是空字串）。password 空白是沿用已經存的。 */
export interface EmailDraft {
  readonly enabled: boolean;
  readonly provider: EmailProvider;
  readonly host: string;
  readonly port: string;
  readonly security: SmtpSecurity;
  readonly username: string;
  readonly password: string;
  readonly fromAddress: string;
  readonly fromName: string;
  readonly siteUrl: string;
}

/** 連接埠只能是這四個（後端也這樣擋：伺服器會照設定去連，任意連接埠等於讓人掃內網）。 */
export const ALLOWED_PORTS: readonly string[] = ['587', '465', '25', '2525'];

/** 每種加密方式慣用的連接埠。 */
const DEFAULT_PORTS: Readonly<Record<SmtpSecurity, string>> = { start_tls: '587', tls: '465', none: '25' };

const GMAIL_FIELDS: readonly EmailField[] = ['username', 'password', 'fromName', 'siteUrl'];
const SMTP_FIELDS: readonly EmailField[] = ['host', 'port', 'security', 'username', 'password', 'fromAddress', 'fromName', 'siteUrl'];

/** 這個寄信服務在畫面上有的欄位：後端的細項只放得到畫得出來的欄位，其他的併到表單上方。 */
export function visibleFields(provider: EmailProvider): readonly EmailField[] {
  return provider === 'gmail' ? GMAIL_FIELDS : SMTP_FIELDS;
}

export interface DraftContext {
  /** 寄件人名稱空的時候預先填的名稱。 */
  readonly organizationName: string;
  /** 網站網址沒存過時預先填的網址。 */
  readonly origin: string;
}

/** 從存著的設定（或沒存過的空設定）做出表單的起點，空的欄位預先填好。 */
export function toDraft(settings: EmailSettings, context: DraftContext): EmailDraft {
  const security = settings.security ?? 'start_tls';
  return {
    enabled: settings.enabled,
    provider: settings.provider,
    host: settings.host ?? '',
    port: settings.port === null ? DEFAULT_PORTS[security] : String(settings.port),
    security,
    username: settings.username ?? '',
    password: '',
    fromAddress: settings.fromAddress ?? '',
    fromName: settings.fromName ?? context.organizationName,
    siteUrl: settings.siteUrl ?? context.origin,
  };
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

/** 只打了空白當作沒填（null：沿用已經存的），其他照原樣送（Gmail 顯示成四組四碼的空白由後端去掉）。 */
function passwordInput(password: string): string | null {
  return password.trim() === '' ? null : password;
}

/**
 * 要送出的本文。Gmail 不送主機、連接埠、加密方式、寄件人地址（存成 null）。
 * 不加密時不能登入：帳號清成 null、密碼送空字串（把已經存的也清掉，不然後端把存著的密碼也算進去而擋下來）。
 * 密碼其他時候空白就是 null（沿用）；Gmail 的應用程式密碼 Google 顯示成四組四碼，後端會去掉空白，這裡照原樣送。
 */
export function toInput(draft: EmailDraft): EmailSettingsInput {
  const common = { enabled: draft.enabled, fromName: blankToNull(draft.fromName), siteUrl: blankToNull(draft.siteUrl) };
  if (draft.provider === 'gmail') {
    return {
      ...common,
      provider: 'gmail',
      host: null,
      port: null,
      security: null,
      username: blankToNull(draft.username),
      password: passwordInput(draft.password),
      fromAddress: null,
    };
  }
  const isPlain = draft.security === 'none';
  return {
    ...common,
    provider: 'smtp',
    host: blankToNull(draft.host),
    port: Number(draft.port),
    security: draft.security,
    username: isPlain ? null : blankToNull(draft.username),
    password: isPlain ? '' : passwordInput(draft.password),
    fromAddress: blankToNull(draft.fromAddress),
  };
}

/** 換加密方式：連接埠還是上一個加密方式的慣用值就跟著換；使用者自己選過別的連接埠就不動。 */
export function withSecurity(draft: EmailDraft, security: SmtpSecurity): EmailDraft {
  const followsDefault = draft.port === DEFAULT_PORTS[draft.security];
  return { ...draft, security, port: followsDefault ? DEFAULT_PORTS[security] : draft.port };
}

export function hasDraftChanges(draft: EmailDraft, baseline: EmailDraft): boolean {
  return (Object.keys(draft) as (keyof EmailDraft)[]).some((key) => draft[key] !== baseline[key]);
}

/**
 * 密碼留白（沿用）時，後端要求寄信服務、主機、連接埠、帳號都和已存的一樣：用別的設定去登入，
 * 不能把已存的密碼送給新的對象。不一樣就要重新輸入密碼（只有已經存了密碼才有這件事）。
 */
export function needsNewPassword(draft: EmailDraft, settings: EmailSettings): boolean {
  if (!settings.hasPassword) return false;
  const input = toInput(draft);
  return (
    input.provider !== settings.provider ||
    input.host !== settings.host ||
    input.port !== settings.port ||
    input.username !== settings.username
  );
}
