import { SelectField, TextField } from '../../components/Field/Field';
import { PasswordField } from '../../components/Field/PasswordField';
import type { EmailSettings, SmtpSecurity } from '../../api/emailSettings';
import { ALLOWED_PORTS, needsNewPassword, type EmailDraft } from './emailSettingsDraft';
import type { EmailSettingsFormState } from './useEmailSettingsForm';
import styles from './EmailSettings.module.css';

const SECURITY_OPTIONS: readonly { readonly value: SmtpSecurity; readonly label: string }[] = [
  { value: 'start_tls', label: 'STARTTLS' },
  { value: 'tls', label: 'SSL／TLS' },
  { value: 'none', label: '不加密' },
];

const PORT_OPTIONS = ALLOWED_PORTS.map((port) => ({ value: port, label: port }));
const PLAIN_NOTE = '不加密時不能登入，帳號密碼會用明文送出';
const PLAIN_CLEARS_STORED = '儲存後會清掉已存的帳號與密碼。';
const PASSWORD_STORED = '已經存了，不改就留白';
const PASSWORD_CHANGED = '改了寄信服務、主機、連接埠或帳號，要重新輸入密碼。';
const GMAIL_APP_PASSWORDS_URL = 'https://myaccount.google.com/apppasswords';

interface FieldsProps {
  readonly form: EmailSettingsFormState;
  /** 存著的設定：已經存了密碼時，密碼欄留白就是沿用；改了登入的對象就要重新輸入。 */
  readonly settings: EmailSettings;
}

interface CredentialHints {
  readonly account: string | undefined;
  readonly password: string | undefined;
}

/** 帳號與密碼欄的說明：不加密時兩欄都說明不能登入；其他時候只有密碼欄，說沿用或要重新輸入。 */
function credentialHints(draft: EmailDraft, settings: EmailSettings): CredentialHints {
  if (draft.provider === 'smtp' && draft.security === 'none') {
    const note = settings.hasPassword ? `${PLAIN_NOTE}。${PLAIN_CLEARS_STORED}` : PLAIN_NOTE;
    return { account: note, password: note };
  }
  if (!settings.hasPassword) return { account: undefined, password: undefined };
  return { account: undefined, password: needsNewPassword(draft, settings) ? PASSWORD_CHANGED : PASSWORD_STORED };
}

/** 和錯誤同一句的說明不再顯示：後端回的錯誤就是這句話，疊兩次只是重複。 */
function hintUnlessError(hint: string | undefined, error: string | undefined): string | undefined {
  return hint === error ? undefined : hint;
}

/** 帳號與密碼：Gmail 與其他 SMTP 共用同一組草稿，只有標籤不同；不加密時不能登入，兩欄停用。 */
function CredentialFields({ form, settings }: FieldsProps) {
  const { draft, errors, change } = form;
  const hints = credentialHints(draft, settings);
  const isGmail = draft.provider === 'gmail';
  const isPlain = !isGmail && draft.security === 'none';
  return (
    <>
      <TextField
        label={isGmail ? 'Gmail 地址' : '帳號'}
        type={isGmail ? 'email' : 'text'}
        autoComplete="off"
        maxLength={254}
        disabled={isPlain}
        hint={hintUnlessError(hints.account, errors.username)}
        value={isPlain ? '' : draft.username}
        error={errors.username}
        onChange={(event) => {
          change({ username: event.target.value }, 'username');
        }}
      />
      <PasswordField
        label={isGmail ? '應用程式密碼' : '密碼'}
        autoComplete="new-password"
        maxLength={256}
        disabled={isPlain}
        value={isPlain ? '' : draft.password}
        hint={hintUnlessError(hints.password, errors.password)}
        error={errors.password}
        onChange={(event) => {
          change({ password: event.target.value }, 'password');
        }}
      />
    </>
  );
}

function GmailNote() {
  return (
    <p className={styles.note}>
      先在 Google 帳戶開啟兩步驟驗證，再到
      <a href={GMAIL_APP_PASSWORDS_URL} target="_blank" rel="noopener noreferrer">
        「應用程式密碼」
      </a>
      產生一組 16 碼的密碼貼在這裡。
    </p>
  );
}

function SmtpServerFields({ form }: { readonly form: EmailSettingsFormState }) {
  const { draft, errors, change, changeSecurity } = form;
  return (
    <>
      <TextField
        label="主機"
        autoComplete="off"
        spellCheck={false}
        maxLength={253}
        value={draft.host}
        error={errors.host}
        onChange={(event) => {
          change({ host: event.target.value }, 'host');
        }}
      />
      <div className={styles.pair}>
        <SelectField
          label="連接埠"
          value={draft.port}
          options={PORT_OPTIONS}
          error={errors.port}
          onChange={(port) => {
            change({ port }, 'port');
          }}
        />
        <SelectField
          label="加密方式"
          value={draft.security}
          options={SECURITY_OPTIONS}
          error={errors.security}
          onChange={(security) => {
            changeSecurity(security as SmtpSecurity);
          }}
        />
      </div>
    </>
  );
}

/** 「寄信服務」那一塊的欄位：SMTP 才有主機、連接埠與加密方式；帳號與密碼兩種都有；Gmail 多一段怎麼拿應用程式密碼。 */
export function EmailServiceFields({ form, settings }: FieldsProps) {
  const isGmail = form.draft.provider === 'gmail';
  return (
    <div className={styles.fields}>
      {!isGmail && <SmtpServerFields form={form} />}
      <CredentialFields form={form} settings={settings} />
      {isGmail && <GmailNote />}
    </div>
  );
}

/** 「寄件人」那一塊的欄位：寄件人地址只有 SMTP 要填（Gmail 就是登入的地址）；順序照 emailSettingsDraft 的 visibleFields。 */
export function EmailSenderFields({ form }: { readonly form: EmailSettingsFormState }) {
  const { draft, errors, change } = form;
  return (
    <div className={styles.fields}>
      {draft.provider !== 'gmail' && (
        <TextField
          label="寄件人地址"
          type="email"
          autoComplete="off"
          maxLength={254}
          value={draft.fromAddress}
          error={errors.fromAddress}
          onChange={(event) => {
            change({ fromAddress: event.target.value }, 'fromAddress');
          }}
        />
      )}
      <TextField
        label="寄件人名稱"
        autoComplete="off"
        maxLength={100}
        value={draft.fromName}
        error={errors.fromName}
        onChange={(event) => {
          change({ fromName: event.target.value }, 'fromName');
        }}
      />
      <TextField
        label="網站網址"
        type="url"
        autoComplete="off"
        spellCheck={false}
        maxLength={200}
        hint="信裡的連結用"
        value={draft.siteUrl}
        error={errors.siteUrl}
        onChange={(event) => {
          change({ siteUrl: event.target.value }, 'siteUrl');
        }}
      />
    </div>
  );
}
