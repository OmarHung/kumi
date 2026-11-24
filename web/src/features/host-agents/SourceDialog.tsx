import { useEffect, useId, type SubmitEvent } from 'react';
import type { AiModelOption, LocalEngineKind } from '../../api/aiTypes';
import type { HostAgent, HostSource } from '../../api/hostAgentTypes';
import { CheckboxField, CheckboxGroup } from '../../components/CheckboxField/CheckboxField';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { SelectField, TextField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { Notice } from '../../components/Notice/Notice';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import { DEFAULT_EFFORT_LABEL, effortLabel } from '../ai/aiLabels';
import { BILLING_TEXT, engineLabel, HOST_ENGINES, READINESS_TEXT } from './hostAgentText';
import { takenEngines } from './sourceDraft';
import { useSourceForm, type SourceForm } from './useSourceForm';
import styles from './HostAgents.module.css';

const BILLING_LABEL = '我了解這會以 API 用量計費（計到登入的 Console 組織或 OpenAI 帳號），不是訂閱的額度';
const NONE = '';

function TargetFields({ form, isNew }: { readonly form: SourceForm; readonly isNew: boolean }) {
  const { draft, agent } = form;
  if (!isNew) {
    return <p className={styles.note}>{`${agent?.name ?? '（已移除的代理）'} · ${engineLabel(draft.engine)}（建立之後不能換）`}</p>;
  }
  return (
    <>
      <SelectField
        label="主機代理"
        value={draft.agentId}
        options={form.agents.map((candidate) => ({ value: candidate.id, label: candidate.name }))}
        placeholder="先新增一台主機代理"
        error={form.errors.agentId}
        onChange={(agentId) => {
          form.changeTarget(agentId, draft.engine);
        }}
      />
      <EngineChoice form={form} />
    </>
  );
}

/** 引擎的切換：這台代理已經有來源的引擎不列出來並說明；後端的 engine 錯誤顯示在下面，焦點找得到。 */
function EngineChoice({ form }: { readonly form: SourceForm }) {
  const { draft } = form;
  const errorId = useId();
  const taken = takenEngines(draft.agentId, form.sources);
  const options = HOST_ENGINES.filter((engine) => engine === draft.engine || !taken.includes(engine)).map((engine) => ({ value: engine, label: engineLabel(engine) }));
  const error = form.errors.engine;
  return (
    <div className={styles.engineChoice} data-field-error={error === undefined ? undefined : 'engine'}>
      <SegmentedControl<LocalEngineKind>
        label="引擎"
        options={options}
        value={draft.engine}
        describedBy={error === undefined ? undefined : errorId}
        onChange={(engine) => {
          form.changeTarget(draft.agentId, engine);
        }}
      />
      {taken.length > 0 && <p className={styles.note}>{`${taken.map((engine) => engineLabel(engine)).join('、')} 在這台代理上已經有來源了，不能再新增。`}</p>}
      <Notice tone="danger" id={errorId}>
        {error}
      </Notice>
    </div>
  );
}

/** 引擎現在不能接工作、或登入的不是 API 帳號：先說清楚（可以先存成停用的，登入之後再開）。 */
function EngineNotice({ agent, engine }: { readonly agent: HostAgent | undefined; readonly engine: LocalEngineKind }) {
  const status = agent?.engines.find((candidate) => candidate.kind === engine);
  if (agent === undefined || status?.readiness === 'ready') return null;
  const state = status === undefined ? '還沒回報' : READINESS_TEXT[status.readiness];
  const billing = status?.loggedIn === true && status.billing !== 'api' ? `；登入的是「${BILLING_TEXT[status.billing]}」，不能開成組織的來源` : '';
  return (
    <Notice tone="info" role="none">
      {`${engineLabel(engine)} 現在是「${state}」${billing}。可以先存成停用的，登入並可以接工作之後再開。`}
    </Notice>
  );
}

function ModelChoices({ form }: { readonly form: SourceForm }) {
  const { draft, choices } = form;
  return (
    <>
      <CheckboxGroup legend="開放的模型" layout="column" hint={choices.length === 0 ? '引擎還沒回報模型清單（還沒登入，或主機離線）。' : undefined}>
        {choices.map((model) => (
          <CheckboxField
            key={model.id}
            label={
              <span>
                {model.displayName}
                {model.efforts.length > 0 && <small className={styles.modelDetail}>{`思考深度 ${model.efforts.map((effort) => effortLabel(effort)).join('／')}`}</small>}
              </span>
            }
            checked={draft.enabledModels.includes(model.id)}
            onChange={() => {
              form.toggleModel(model.id);
            }}
          />
        ))}
      </CheckboxGroup>
      <Notice tone="danger">{form.errors.enabledModels}</Notice>
    </>
  );
}

function DefaultFields({ form }: { readonly form: SourceForm }) {
  const { draft, choices } = form;
  const enabled = choices.filter((model) => draft.enabledModels.includes(model.id));
  const efforts = enabled.find((model: AiModelOption) => model.id === draft.defaultModel)?.efforts ?? [];
  return (
    <>
      <SelectField
        label="預設的模型"
        value={draft.defaultModel}
        options={[{ value: NONE, label: '清單的第一個' }, ...enabled.map((model) => ({ value: model.id, label: model.displayName }))]}
        error={form.errors.defaultModel}
        onChange={(value) => {
          form.change({ defaultModel: value, defaultEffort: '' }, 'defaultModel');
        }}
      />
      <SelectField
        label="預設的思考深度"
        value={draft.defaultEffort}
        disabled={efforts.length === 0}
        error={form.errors.defaultEffort}
        hint={efforts.length === 0 ? '先選預設的模型；模型不能選思考深度時不用設' : undefined}
        options={[{ value: NONE, label: DEFAULT_EFFORT_LABEL }, ...efforts.map((effort) => ({ value: effort, label: effortLabel(effort) }))]}
        onChange={(value) => {
          form.change({ defaultEffort: efforts.find((effort) => effort === value) ?? '' }, 'defaultEffort');
        }}
      />
    </>
  );
}

function LimitFields({ form }: { readonly form: SourceForm }) {
  const { draft, errors } = form;
  const field = (name: 'dailyRequestsPerPerson' | 'monthlyRequests' | 'monthlyTokens', label: string, hint?: string) => (
    <TextField
      label={label}
      inputMode="numeric"
      autoComplete="off"
      placeholder="不限"
      hint={hint}
      value={draft[name]}
      error={errors[name]}
      onChange={(event) => {
        form.change({ [name]: event.target.value }, name);
      }}
    />
  );
  return (
    <div className={styles.limits}>
      {field('dailyRequestsPerPerson', '每人每天最多（則）')}
      {field('monthlyRequests', '每月最多（則）', '全組織合計')}
      {field('monthlyTokens', '每月 token 上限', '輸入加輸出，只看已結束的')}
    </div>
  );
}

/** 名稱、模型、預設、上限、開關與計費確認（對話框的表單內容）。 */
function SourceFields({ form, isNew }: { readonly form: SourceForm; readonly isNew: boolean }) {
  const { draft } = form;
  return (
    <>
      <TargetFields form={form} isNew={isNew} />
      <EngineNotice agent={form.agent} engine={draft.engine} />
      <TextField
        label="名稱"
        hint="成員在模型選單看到的名稱"
        autoComplete="off"
        maxLength={80}
        value={draft.name}
        error={form.errors.name}
        onChange={(event) => {
          form.change({ name: event.target.value }, 'name');
        }}
      />
      <ModelChoices form={form} />
      <DefaultFields form={form} />
      <LimitFields form={form} />
      <CheckboxField
        label="開放給成員使用"
        hint="關著時成員的模型選單裡沒有這個來源。"
        checked={draft.enabled}
        onChange={(event) => {
          form.change({ enabled: event.target.checked }, 'enabledModels');
        }}
      />
      <CheckboxField
        label={BILLING_LABEL}
        checked={draft.acknowledged}
        aria-invalid={form.errors.acknowledgeApiBilling === undefined ? undefined : true}
        onChange={(event) => {
          form.change({ acknowledged: event.target.checked }, 'acknowledgeApiBilling');
        }}
      />
      <Notice tone="danger">{form.errors.acknowledgeApiBilling}</Notice>
    </>
  );
}

interface SourceDialogProps {
  /** null 是新增。 */
  readonly source: HostSource | null;
  readonly agents: readonly HostAgent[];
  readonly sources: readonly HostSource[];
  readonly onClose: () => void;
}

/** 新增、編輯組織來源：代理與引擎（新增時選）、名稱、模型、預設、上限、開關，以及開著時必勾的計費確認。 */
export function SourceDialog({ source, agents, sources, onClose }: SourceDialogProps) {
  const form = useSourceForm({ source, agents, sources, onSaved: onClose });
  const { formRef, focusCount } = form;
  useEffect(() => {
    if (focusCount > 0) formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"], [data-field-error] button')?.focus();
  }, [focusCount, formRef]);

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    form.save();
  }

  return (
    <Dialog title={source === null ? '新增來源' : '編輯來源'} size="lg" onClose={form.isSaving ? () => undefined : onClose}>
      <form ref={formRef} className={styles.dialogForm} noValidate aria-busy={form.isSaving} onSubmit={handleSubmit}>
        <SourceFields form={form} isNew={source === null} />
        <FormAlert message={form.formError} />
        <DialogFormActions submitLabel="儲存" pendingLabel="儲存中…" isPending={form.isSaving} keepsFocusWhilePending onCancel={onClose} />
      </form>
    </Dialog>
  );
}
