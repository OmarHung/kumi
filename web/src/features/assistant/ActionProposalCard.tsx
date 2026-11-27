import { useRef, useState } from 'react';
import { Link } from 'react-router';
import type { ActionInput, ActionProposal, ActionStep } from '../../api/aiTypes';
import { useActionProposal, useDecideActionProposal, type ActionInputValues } from '../../api/actionProposals';
import { isApiError } from '../../api/http';
import { PasswordField } from '../../components/Field/PasswordField';
import { TextField } from '../../components/Field/Field';
import { Icon } from '../../components/Icon/Icon';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { ClosedByOtherNotice, ProposalActions } from './ProposalActions';
import { DescriptionBox } from './ProposalCard';
import { ProposalShell } from './ProposalShell';
import styles from './ProposalCard.module.css';

const CLOSED_CODE = 'ai.action_proposal_closed';
const INPUT_PATH_PREFIX = 'inputs.';
const UNFILLED_TEXT = '還有欄位沒填';
/** 只有站內的路徑才畫成連結：伺服器給的字串不當成可以跳到別的網站。 */
const isInternalPath = (link: string) => link.startsWith('/') && !link.startsWith('//');
const NO_INPUTS: ActionInputValues = {};

/** 後端 422 的 details 路徑是 inputs.<key>：對得到欄位的錯誤顯示在那個欄位上。 */
function fieldErrorsOf(error: unknown): ReadonlyMap<string, string> {
  const errors = new Map<string, string>();
  if (!isApiError(error)) return errors;
  for (const detail of error.details) {
    if (detail.path?.startsWith(INPUT_PATH_PREFIX) === true) errors.set(detail.path.slice(INPUT_PATH_PREFIX.length), detail.message);
  }
  return errors;
}

function TextCompare({ step }: { readonly step: ActionStep }) {
  if (step.before === null && step.after === null) return null;
  return (
    <p className={styles.draftMeta}>
      {step.before !== null && (
        <>
          <span className={styles.before}>{step.before}</span>
          {step.after !== null && (
            <>
              <span className="visually-hidden"> 改成 </span>
              <span aria-hidden="true" className={styles.arrow}>
                {' → '}
              </span>
            </>
          )}
        </>
      )}
      {step.after !== null && <span className={styles.after}>{step.after}</span>}
    </p>
  );
}

function MarkdownCompare({ step }: { readonly step: ActionStep }) {
  if (step.before === null && step.after === null) return null;
  return (
    <div className={styles.compare}>
      {step.before !== null && <DescriptionBox label={`現在的內容：${step.label}`} heading="現在的" text={step.before} />}
      {step.after !== null && <DescriptionBox label={`建議的內容：${step.label}`} heading="建議的" text={step.after} linkHosts="always" />}
    </div>
  );
}

function StepRow({ step }: { readonly step: ActionStep }) {
  return (
    <li className={styles.draft}>
      <p className={styles.draftTitle}>{step.label}</p>
      {step.target !== null && <p className={styles.draftMeta}>{step.target}</p>}
      {step.format === 'markdown' ? <MarkdownCompare step={step} /> : <TextCompare step={step} />}
      {step.note !== null && <p className={styles.commentNote}>{step.note}</p>}
      {step.error !== null && <Notice tone="warning">{step.error}</Notice>}
      {step.done && step.error === null && (
        <p className={styles.entryResult}>
          <Icon name="check" />
          <span>已套用</span>
          {step.link !== null && isInternalPath(step.link) && <Link to={step.link} aria-label={`查看 ${step.target ?? step.label}`}>查看</Link>}
        </p>
      )}
    </li>
  );
}

function StepList({ steps }: { readonly steps: readonly ActionStep[] }) {
  return (
    <ol className={styles.drafts}>
      {steps.map((step, index) => (
        <StepRow key={index} step={step} />
      ))}
    </ol>
  );
}

interface InputFieldsProps {
  readonly inputs: readonly ActionInput[];
  readonly values: ActionInputValues;
  readonly onChange: (key: string, value: string) => void;
  readonly isDisabled: boolean;
  readonly errors: ReadonlyMap<string, string>;
}

/** 卡片輸入：值只放在元件的 state（不進快取、不進瀏覽器儲存空間），送出後清掉。 */
function InputFields({ inputs, values, onChange, isDisabled, errors }: InputFieldsProps) {
  return (
    <>
      {inputs.map((input) => {
        const common = {
          label: input.label,
          hint: input.hint ?? undefined,
          value: values[input.key] ?? '',
          disabled: isDisabled,
          required: true,
          error: errors.get(input.key),
          onChange: (event: { readonly target: { readonly value: string } }) => {
            onChange(input.key, event.target.value);
          },
        };
        return input.kind === 'password' ? (
          <PasswordField key={input.key} autoComplete="new-password" data-1p-ignore data-lpignore="true" data-bwignore {...common} />
        ) : (
          <TextField key={input.key} autoComplete="off" {...common} />
        );
      })}
    </>
  );
}

function areInputsFilled(inputs: readonly ActionInput[], values: ActionInputValues): boolean {
  return inputs.every((input) => (values[input.key] ?? '').trim() !== '');
}

function PendingBody({ proposal }: { readonly proposal: ActionProposal }) {
  const [values, setValues] = useState<ActionInputValues>(NO_INPUTS);
  // 送出的那一刻才讀：值不當成 mutation 的變數，免得留在 mutation 快取裡。
  const valuesRef = useRef<ActionInputValues>(NO_INPUTS);
  const clear = () => {
    valuesRef.current = NO_INPUTS;
    setValues(NO_INPUTS);
  };
  const { apply, dismiss } = useDecideActionProposal(proposal, () => valuesRef.current, clear);
  const isWorking = apply.isPending || dismiss.isPending;
  return (
    <>
      <StepList steps={proposal.steps} />
      <InputFields
        inputs={proposal.inputs}
        values={values}
        isDisabled={isWorking}
        errors={fieldErrorsOf(apply.error)}
        onChange={(key, value) => {
          valuesRef.current = { ...valuesRef.current, [key]: value };
          setValues(valuesRef.current);
        }}
      />
      <ProposalActions
        apply={apply}
        dismiss={dismiss}
        applyLabel="套用"
        busyLabel="套用中…"
        closedCode={CLOSED_CODE}
        isApplyBlocked={!areInputsFilled(proposal.inputs, values)}
        applyBlockedReason={UNFILLED_TEXT}
      />
    </>
  );
}

function ClosedSummary({ proposal }: { readonly proposal: ActionProposal }) {
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  if (proposal.status === 'dismissed') return <p className={styles.status}>{`已略過${when}`}</p>;
  return (
    <>
      <p className={styles.summary}>{`已套用${when}`}</p>
      {proposal.error !== null && <Notice tone="warning">{proposal.error}</Notice>}
    </>
  );
}

/** role="status" 只放摘要行（套用中、已套用）：步驟列表在外面，每次重讀不會整份被唸一遍。 */
function LoadedCard({ proposal }: { readonly proposal: ActionProposal }) {
  const isClosed = proposal.status === 'applied' || proposal.status === 'dismissed';
  return (
    <section className={styles.card} aria-label={`動作建議 ${proposal.title}`}>
      <p className={styles.title}>{proposal.title}</p>
      {proposal.reason !== null && <p className={styles.summary}>{proposal.reason}</p>}
      {proposal.status === 'pending' && <PendingBody proposal={proposal} />}
      <div role="status">
        {proposal.status !== 'pending' && <ClosedByOtherNotice proposalId={proposal.id} />}
        {proposal.status === 'applying' && <p className={styles.status}>套用中…</p>}
        {isClosed && <ClosedSummary proposal={proposal} />}
      </div>
      {proposal.status !== 'pending' && proposal.status !== 'dismissed' && <StepList steps={proposal.steps} />}
    </section>
  );
}

/**
 * 助理的知識庫、人員、附件、存的檢視、說明格式建議（通用的動作建議，internal/HANDOFF-ai-coverage-2.md 第 3 節）：
 * 步驟的內容由伺服器產生，這裡照畫；按「套用」才以自己的身分一步一步寫入。
 */
export function ActionProposalCard({ proposalId }: { readonly proposalId: string }) {
  const query = useActionProposal(proposalId);
  return (
    <ProposalShell query={query} noun="動作建議">
      {(proposal) => <LoadedCard proposal={proposal} />}
    </ProposalShell>
  );
}
