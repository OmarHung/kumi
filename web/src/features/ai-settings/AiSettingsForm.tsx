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

const NO_MODELS = '先新增連線、開放至少一個模型';
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

const LIMITS_NOTE = '只算組織的連線；成員自己的 AI 帳號、本機代理、主機上的 AI 不算（主機來源有自己的上限）。留白是不限。';

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

const ASSISTANT_DESCRIPTION = '專案的 AI 助理用的模型與預設。費用算在你們自己的供應商帳號。';
const CONNECTIONS_DESCRIPTION = '接上 AI 供應商，開放成員可以選的模型。';

/** 每次送出後有欄位出錯（focusCount 加一）就把焦點移到第一個有問題的欄位。 */
function useFocusFirstInvalid(focusCount: number) {
  const formRef = useRef<HTMLFormElement>(null);
  useEffect(() => {
    if (focusCount > 0) formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }, [focusCount]);
  return formRef;
}

/** 結果和按鈕同一列、寫在按鈕旁邊：不佔額外高度，下面的連線不被推開。狀態那個一直掛著：之後才填進來的字，報讀軟體才唸得到。 */
function SaveActions({ form }: { readonly form: AiSettingsFormState }) {
  return (
    <div className={styles.actions}>
      <Button variant="primary" type="submit" aria-disabled={form.isSaving || undefined}>
        {form.isSaving ? '儲存中…' : '儲存'}
      </Button>
      <span className={styles.result} data-tone="success" role="status">
        {form.message}
      </span>
      {form.formError !== null && (
        <span className={styles.result} data-tone="danger" role="alert">
          {form.formError}
        </span>
      )}
    </div>
  );
}

/** 兩塊：「AI 助理」（啟用、預設與儲存）與「連線」（連線在對話框裡各自存，不跟儲存鈕一起）。 */
export function AiSettingsForm({ settings }: { readonly settings: AiSettings }) {
  const form = useAiSettingsForm(settings);
  const formRef = useFocusFirstInvalid(form.focusCount);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    form.save();
  }

  return (
    <>
      <SettingsSection title="AI 助理" description={<p>{ASSISTANT_DESCRIPTION}</p>} isContentStart>
        {/* 表單不另外取名：外面的區塊已經是有名稱的 region「AI 助理」，再取一樣的名字報讀軟體會列兩個同名的地標。 */}
        <form ref={formRef} className={styles.form} noValidate aria-busy={form.isSaving} onSubmit={handleSubmit}>
          <CheckboxField
            label="啟用 AI 助理"
            hint="開著時，專案成員可以在專案的「AI 助理」選下面開放的模型，請它幫忙調整專案的設定。"
            checked={form.enabled}
            onChange={(event) => {
              form.setEnabled(event.target.checked);
            }}
          />
          <CheckboxField
            label="允許成員使用自己的 AI 帳號"
            hint="開了之後，每個人可以在設定的「AI 帳號」接自己的帳號，只有自己能用、費用算自己的帳號；專案的設定會送到他們選的供應商。也包括成員自己電腦上的 Claude Code／Codex。"
            checked={form.allowPersonal}
            onChange={(event) => {
              form.setAllowPersonal(event.target.checked);
            }}
          />
          <DefaultSelection form={form} />
          <UsageLimits form={form} settings={settings} />
          <SaveActions form={form} />
          {form.guardDialog}
        </form>
      </SettingsSection>
      <ConnectionList connections={settings.connections} description={CONNECTIONS_DESCRIPTION} />
    </>
  );
}
