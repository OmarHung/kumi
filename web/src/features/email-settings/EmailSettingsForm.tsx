import { useEffect, type SubmitEvent } from 'react';
import type { EmailProvider, EmailSettings } from '../../api/emailSettings';
import { Button } from '../../components/Button/Button';
import { CheckboxField } from '../../components/CheckboxField/CheckboxField';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { formatInstant } from '../../domain/time';
import { EmailSenderFields, EmailServiceFields } from './EmailSettingsFields';
import { useEmailSettingsForm, type EmailSettingsFormState } from './useEmailSettingsForm';
import styles from './EmailSettings.module.css';

const PROVIDER_OPTIONS: readonly { readonly value: EmailProvider; readonly label: string }[] = [
  { value: 'gmail', label: 'Gmail' },
  { value: 'smtp', label: '其他 SMTP' },
];

/** 區塊的標題已經是「寄信服務」，所以選項前面不再重複寫一次標籤（名稱留給報讀軟體）。 */
function ProviderSwitch({ value, onChange }: { readonly value: EmailProvider; readonly onChange: (provider: EmailProvider) => void }) {
  return (
    <div className={styles.provider}>
      <SegmentedControl label="寄信服務" options={PROVIDER_OPTIONS} value={value} onChange={onChange} />
    </div>
  );
}

/** 上次寄送的狀況：失敗的時間比寄出的新才是失敗（寄出成功之後 lastFailedAt 還留著，網頁比較兩個時間決定顯示哪一個）。 */
function DeliveryStatus({ settings }: { readonly settings: EmailSettings }) {
  const { lastSentAt, lastFailedAt, lastError } = settings.status;
  const isFailing = lastFailedAt !== null && (lastSentAt === null || Date.parse(lastFailedAt) > Date.parse(lastSentAt));
  if (isFailing) {
    return (
      <Notice tone="warning" role="none">
        {`上次寄送失敗（${formatInstant(lastFailedAt)}）：${lastError ?? '原因不明'}`}
      </Notice>
    );
  }
  return lastSentAt === null ? null : <p className={styles.note}>{`上次寄出：${formatInstant(lastSentAt)}`}</p>;
}

/** 送出中不停用、改標 aria-disabled：焦點就在按鈕上，停用會讓焦點掉到頁面本體。 */
function FormActions({ form }: { readonly form: EmailSettingsFormState }) {
  return (
    <div className={styles.actions}>
      <Button variant="primary" type="submit" aria-disabled={form.isBusy || undefined}>
        {form.isSaving ? '儲存中…' : '儲存'}
      </Button>
      <Button aria-disabled={form.isBusy || undefined} onClick={form.sendTest}>
        {form.isSendingTest ? '寄送中…' : '寄測試信給我'}
      </Button>
    </div>
  );
}

interface EmailSettingsFormProps {
  readonly settings: EmailSettings;
  readonly organizationName: string;
}

/**
 * 寄信設定的表單：一個 form 跨四塊（寄送、寄信服務、寄件人、狀況與測試），一次送出。
 * 儲存與「寄測試信給我」在最後一塊的右欄，結果的提示也在那裡（一直掛著、不推動版面）。
 * 「寄送」不寫左欄的說明：勾選框和它的提示已經說完，再寫一句只是重複。
 */
export function EmailSettingsForm({ settings, organizationName }: EmailSettingsFormProps) {
  const form = useEmailSettingsForm(settings, { organizationName, origin: window.location.origin });
  const { draft, formRef, focusCount } = form;
  // 送出後有問題：焦點到第一個標了無效的欄位（Select 的按鈕也有 aria-invalid）。
  useEffect(() => {
    if (focusCount > 0) formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [focusCount, formRef]);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    form.save();
  }

  return (
    <form ref={formRef} noValidate aria-busy={form.isBusy} onSubmit={handleSubmit}>
      <SettingsSection title="寄送" isContentStart>
        <CheckboxField
          label="寄送 Email 通知"
          hint="開著時，沒讀的通知 5 分鐘後寄到每個人的 Email；每個人可以在自己的「通知」設定關掉。"
          checked={draft.enabled}
          onChange={(event) => {
            form.change({ enabled: event.target.checked });
          }}
        />
      </SettingsSection>
      <SettingsSection title="寄信服務" description="用 Gmail，或自己的 SMTP 伺服器寄出。">
        <div className={styles.stack}>
          <ProviderSwitch value={draft.provider} onChange={form.changeProvider} />
          <EmailServiceFields form={form} settings={settings} />
        </div>
      </SettingsSection>
      <SettingsSection title="寄件人" description="收件人在信上看到的寄件人。">
        <EmailSenderFields form={form} />
      </SettingsSection>
      <SettingsSection title="儲存與測試" description="儲存之後，可以先寄一封測試信給自己確認。">
        <div className={styles.stack}>
          <div className={styles.narrow}>
            <DeliveryStatus settings={settings} />
          </div>
          <FormActions form={form} />
          {/* 一直掛著（沒有訊息時看不見、不佔位置）：之後才填進來的說明，報讀軟體才唸得到。 */}
          <div className={styles.narrow}>
            <Notice tone="success" role="status">
              {form.message}
            </Notice>
            <FormAlert message={form.formError} />
          </div>
        </div>
      </SettingsSection>
      {form.guardDialog}
    </form>
  );
}
