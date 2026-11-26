import { Link, useMatch } from 'react-router';
import type { ProjectProposal } from '../../api/aiTypes';
import { useDecideProjectProposal, useProjectProposal } from '../../api/assistant';
import { errorMessage, isApiError } from '../../api/http';
import { Button, buttonClassName } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { formatInstant } from '../../domain/time';
import { requestAssistantPanel } from '../assistant-panel/panelRequests';
import { projectAssistantPath, projectPagePath } from '../work-items/itemLinks';
import { DescriptionBox } from './ProposalCard';
import styles from './ProposalCard.module.css';

const CLOSED_CODE = 'ai.project_proposal_closed';
const CLOSED_TEXT = '這份建議已經處理過了。';
const KEY_TAKEN_CODE = 'project.key_taken';
const KEY_TAKEN_HINT = '請在對話裡請助理換一個代號。';

function failureText(error: unknown): string {
  if (isApiError(error) && error.code === CLOSED_CODE) return CLOSED_TEXT;
  return errorMessage(error);
}

function Row({ label, children }: { readonly label: string; readonly children: string }) {
  return (
    <div className={styles.row}>
      <dt className={styles.label}>{label}</dt>
      <dd className={styles.value}>{children}</dd>
    </div>
  );
}

function ProposalRows({ proposal }: { readonly proposal: ProjectProposal }) {
  return (
    <dl className={styles.rows}>
      <Row label="代號">{proposal.key}</Row>
      <Row label="名稱">{proposal.name}</Row>
      <Row label="模板">{proposal.templateName}</Row>
      <Row label="範例項目">{proposal.includeExamples ? '附範例項目' : '不附範例'}</Row>
      {proposal.description !== null && proposal.description.trim() !== '' && (
        <div className={styles.row}>
          <dt className={styles.label}>說明</dt>
          <dd className={styles.value}>
            <DescriptionBox label="專案說明" text={proposal.description} linkHosts="always" />
          </dd>
        </div>
      )}
    </dl>
  );
}

function PendingBody({ proposal }: { readonly proposal: ProjectProposal }) {
  const { apply, dismiss } = useDecideProjectProposal(proposal);
  const isWorking = apply.isPending || dismiss.isPending;
  const failure: unknown = apply.error ?? dismiss.error;
  // 代號被用掉：重按只會得到同一個錯誤，要請助理換一個代號。
  const isKeyTaken = isApiError(apply.error) && apply.error.code === KEY_TAKEN_CODE;
  return (
    <>
      <ProposalRows proposal={proposal} />
      <div className={styles.actions}>
        <Button
          variant="primary"
          size="sm"
          aria-disabled={isWorking || isKeyTaken || undefined}
          onClick={() => {
            if (isWorking || isKeyTaken) return;
            dismiss.reset();
            apply.mutate();
          }}
        >
          {apply.isPending ? '建立中…' : '建立專案'}
        </Button>
        <Button
          size="sm"
          aria-disabled={isWorking || undefined}
          onClick={() => {
            if (isWorking) return;
            apply.reset();
            dismiss.mutate();
          }}
        >
          不要
        </Button>
      </div>
      <Notice tone="danger">{failure === null || failure === undefined ? null : failureText(failure)}</Notice>
      {isKeyTaken && <p className={styles.status}>{KEY_TAKEN_HINT}</p>}
    </>
  );
}

/** 建好之後的去處：到專案；在這個專案開 AI 助理（面板切過去；整頁的 AI 助理裡面板收著，改連到專案自己的助理頁）。 */
function CreatedLinks({ projectKey }: { readonly projectKey: string }) {
  const isFullPage = useMatch({ path: '/assistant', end: false }) !== null;
  return (
    <div className={styles.actions}>
      <Link to={projectPagePath(projectKey)} className={buttonClassName('secondary', 'sm')}>
        {`到專案 ${projectKey}`}
      </Link>
      {isFullPage ? (
        <Link to={projectAssistantPath(projectKey)} className={buttonClassName('secondary', 'sm')}>
          在這個專案開 AI 助理
        </Link>
      ) : (
        <Button
          size="sm"
          onClick={() => {
            requestAssistantPanel(projectKey);
          }}
        >
          在這個專案開 AI 助理
        </Button>
      )}
    </div>
  );
}

function ClosedBody({ proposal }: { readonly proposal: ProjectProposal }) {
  const when = proposal.decidedAt === null ? '' : ` · ${formatInstant(proposal.decidedAt)}`;
  const createdKey = proposal.status === 'applied' ? proposal.createdProjectKey : null;
  return (
    <>
      <p className={styles.status}>
        {proposal.status === 'applied' ? '已建立' : '已略過'}
        {when}
      </p>
      {createdKey !== null && <CreatedLinks projectKey={createdKey} />}
    </>
  );
}

/**
 * 助理的 propose_project：列出代號、名稱、模板與說明，按「建立專案」才以自己的身分建立（internal/HANDOFF-org-assistant.md 第 4 節）。
 * 不是組織管理員的人照樣看得到卡片，按下去收到 403 的說明；失敗時建議維持待處理。
 */
export function ProjectProposalCard({ proposalId }: { readonly proposalId: string }) {
  const proposal = useProjectProposal(proposalId);
  if (proposal.isPending) return <p className={styles.loading}>正在載入新專案建議…</p>;
  if (proposal.isError) return <Notice tone="warning">{`讀不到這份新專案建議：${errorMessage(proposal.error)}`}</Notice>;
  const loaded = proposal.data;
  return (
    <section className={styles.card} aria-label={`新專案建議 ${loaded.key}`}>
      <p className={styles.title}>
        建議建立新專案 <span className={styles.code}>{loaded.key}</span> {loaded.name}
      </p>
      {loaded.status === 'pending' ? <PendingBody proposal={loaded} /> : <ClosedBody proposal={loaded} />}
    </section>
  );
}
