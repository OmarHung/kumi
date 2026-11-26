import { useEffect, useRef, type SubmitEvent } from 'react';
import type { AiEffort, AiMonthUsage, AiSettings } from '../../api/aiTypes';
import { Button } from '../../components/Button/Button';
import { CheckboxField } from '../../components/CheckboxField/CheckboxField';
import { SelectField, TextField } from '../../components/Field/Field';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { effortOptions } from './aiSelection';
import { ConnectionList } from './ConnectionList';
import { LIMIT_FIELDS, usageText, type LimitField } from './usageLimits';
import { useAiSettingsForm, type AiSettingsFormState } from './useAiSettingsForm';
import styles from './AiSettings.module.css';

const NO_MODELS = '先在上面新增 API 金鑰、開放至少一個模型';
const NO_EFFORT = '這個模型不能選思考深度';

function DefaultSelection({ form }: { readonly form: AiSettingsFormState }) {
  const hasModels = form.choices.length > 0;
  const hasEfforts = (form.choice?.efforts.length ?? 0) > 0;
  const modelOptions = form.choices.map((choice) => ({ value: choice.value, label: choice.label }));
  return (
    <div className={styles.defaults}>
      <h3 className={styles.sectionTitle}>預設</h3>
      <div className={styles.fields}>
        <SelectField
          label="模型"
          value={form.choice?.value ?? ''}
          options={modelOptions}
          disabled={!hasModels}
          placeholder="不指定"
          hint={hasModels ? undefined : NO_MODELS}
          onChange={form.chooseModel}
        />
        <SelectField
          label="思考深度"
          value={form.selection?.effort ?? ''}
          options={effortOptions(form.choice)}
          disabled={!hasModels || !hasEfforts}
          hint={hasModels && form.choice !== undefined && !hasEfforts ? NO_EFFORT : undefined}
          onChange={(value) => {
            form.chooseEffort(value === '' ? null : (value as AiEffort));
          }}
        />
      </div>
    </div>
  );
}

const LIMIT_LABELS: Readonly<Record<LimitField, string>> = {
  dailyRequestsPerPerson: '每人每天最多（則）',
  monthlyRequests: '全組織每月最多（則）',
  monthlyTokens: '全組織每月 token 上限',
};

const LIMITS_NOTE = '只算組織的 API 金鑰。成員自己的 API 金鑰與本機代理不算，主機 AI 代理在各來源自己設上限。留白是不限。';

function limitHint(field: LimitField, settings: AiSettings): string | undefined {
  const usage: AiMonthUsage = settings.monthUsage;
  switch (field) {
    case 'dailyRequestsPerPerson':
      return '依組織的時區算一天';
    case 'monthlyRequests':
      return usageText(usage.requests, settings.limits.monthlyRequests, '則');
    case 'monthlyTokens':
      return `${usageText(usage.tokens, settings.limits.monthlyTokens, 'token')}；到了上限之後的下一則才擋`;
  }
}

function UsageLimits({ form, settings }: { readonly form: AiSettingsFormState; readonly settings: AiSettings }) {
  return (
    <div className={styles.defaults}>
      <h3 className={styles.sectionTitle}>用量上限</h3>
      <p className={styles.note}>{LIMITS_NOTE}</p>
      <div className={styles.fields}>
        {LIMIT_FIELDS.map((field) => (
          <TextField
            key={field}
            label={LIMIT_LABELS[field]}
            inputMode="numeric"
            autoComplete="off"
            placeholder="不限"
            hint={limitHint(field, settings)}
            value={form.limits[field]}
            error={form.limitErrors[field]}
            onChange={(event) => {
              form.setLimit(field, event.target.value);
            }}
          />
        ))}
      </div>
    </div>
  );
}

const ASSISTANT_DESCRIPTION = '全組織共用。打開後，成員在專案的 AI 助理選得到上面的 API 金鑰開放的模型。不影響主機 AI 代理與成員自己的 AI。';
const CONNECTIONS_DESCRIPTION = '全組織共用。每一筆是一把供應商的 API 金鑰（Anthropic、OpenAI、Gemini、OpenRouter 或相容的服務），按用量計費、算在那個供應商帳號。Claude、ChatGPT 的訂閱不能接在這裡。';
const POLICY_DESCRIPTION = '全組織的政策，管成員自己的 API 金鑰與電腦上的本機代理。開了之後只有本人能用、費用算本人的帳號；關掉時已經接的還在，但只能刪除。';
const POLICY_SAVE_NAME = '儲存（成員自己的 AI）';
const NEED_MODELS_TO_ENABLE = '先在上面新增 API 金鑰、開放至少一個模型才能打開。';

/** 每次送出後有欄位出錯（focusCount 加一）就把焦點移到第一個有問題的欄位。 */
function useFocusFirstInvalid(focusCount: number) {
  const formRef = useRef<HTMLFormElement>(null);
  useEffect(() => {
    if (focusCount > 0) formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [focusCount]);
  return formRef;
}

interface SaveActionsProps {
  readonly isSaving: boolean;
  /** 另一塊正在存：按鈕擋著，但字不換成「儲存中…」。 */
  readonly isBlocked: boolean;
  readonly message: string | null;
  readonly formError: string | null;
  /** 同一頁有兩顆「儲存」時給第二顆的名稱（畫面上照樣寫「儲存」，名稱開頭也是「儲存」）。 */
  readonly accessibleName?: string;
}

/** 結果和按鈕同一列、寫在按鈕旁邊：不佔額外高度，下面的區塊不被推開。狀態那個一直掛著：之後才填進來的字，報讀軟體才唸得到。 */
function SaveActions({ isSaving, isBlocked, message, formError, accessibleName }: SaveActionsProps) {
  return (
    <div className={styles.actions}>
      <Button variant="primary" type="submit" aria-disabled={isSaving || isBlocked || undefined} aria-label={accessibleName}>
        {isSaving ? '儲存中…' : '儲存'}
      </Button>
      <span className={styles.result} data-tone="success" role="status">
        {message}
      </span>
      {formError !== null && (
        <span className={styles.result} data-tone="danger" role="alert">
          {formError}
        </span>
      )}
    </div>
  );
}

/** 「開放給成員」：組織的 API 金鑰開不開、預設與用量上限。沒有開放任何模型時不能打開（已經開著的照樣能關）。 */
function AssistantSection({ form, settings }: { readonly form: AiSettingsFormState; readonly settings: AiSettings }) {
  const formRef = useFocusFirstInvalid(form.focusCount);
  const canEnable = form.enabled || form.choices.length > 0;
  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    form.save();
  }
  return (
    <SettingsSection title="開放給成員" description={<p>{ASSISTANT_DESCRIPTION}</p>}>
      {/* 表單不另外取名：外面的區塊已經是有名稱的 region，再取一樣的名字報讀軟體會列兩個同名的地標。 */}
      <form ref={formRef} className={styles.form} noValidate aria-busy={form.isSaving} onSubmit={handleSubmit}>
        <CheckboxField
          label="開放組織的 API 金鑰給成員使用"
          hint={canEnable ? undefined : NEED_MODELS_TO_ENABLE}
          checked={form.enabled}
          disabled={!canEnable}
          onChange={(event) => {
            form.setEnabled(event.target.checked);
          }}
        />
        <DefaultSelection form={form} />
        <UsageLimits form={form} settings={settings} />
        <SaveActions isSaving={form.isSaving} isBlocked={form.isAnySaving} message={form.message} formError={form.formError} />
      </form>
    </SettingsSection>
  );
}

/** 「成員自己的 AI」：允不允許成員接自己的 API 金鑰與電腦上的 Claude Code／Codex。自己一個表單、自己儲存。 */
function PolicySection({ form }: { readonly form: AiSettingsFormState }) {
  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    form.savePolicy();
  }
  return (
    <SettingsSection title="成員自己的 AI" description={<p>{POLICY_DESCRIPTION}</p>}>
      <form className={styles.form} noValidate aria-busy={form.isPolicySaving} onSubmit={handleSubmit}>
        <CheckboxField
          label="允許成員使用自己的 AI 帳號"
          hint="成員在設定的「AI 帳號」接自己的 API 金鑰，或用自己電腦上的 Claude Code／Codex（本機代理，可以用自己的訂閱）。專案的設定會送到他們選的供應商。"
          checked={form.allowPersonal}
          onChange={(event) => {
            form.setAllowPersonal(event.target.checked);
          }}
        />
        <SaveActions isSaving={form.isPolicySaving} isBlocked={form.isAnySaving} message={form.policyMessage} formError={form.policyFormError} accessibleName={POLICY_SAVE_NAME} />
      </form>
    </SettingsSection>
  );
}

/**
 * 三塊（四種接法的對照在外面的頁面，讀取狀態之外）：「組織的 API 金鑰」（連線在對話框裡各自存）、「開放給成員」與「成員自己的 AI」（各自一個表單、各自儲存）。
 * API 金鑰排在開放前面：要先有開放的模型才能打開，照做的順序由上往下。
 */
export function AiSettingsForm({ settings }: { readonly settings: AiSettings }) {
  const form = useAiSettingsForm(settings);
  return (
    <>
      <ConnectionList connections={settings.connections} description={CONNECTIONS_DESCRIPTION} />
      <AssistantSection form={form} settings={settings} />
      <PolicySection form={form} />
      {form.guardDialog}
    </>
  );
}
