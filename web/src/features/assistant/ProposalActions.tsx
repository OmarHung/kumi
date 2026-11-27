import type { UseMutationResult } from '@tanstack/react-query';
import { useProposalClosedNotice } from '../../api/proposalDecision';
import { errorMessage, isApiError } from '../../api/http';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import styles from './ProposalCard.module.css';

const CLOSED_TEXT = '這份建議已經處理過了。';

/** 別的分頁已經處理過時留在卡片層級（狀態區）的說明：待處理的區塊重讀後會卸載，訊息不能跟著消失。 */
export function ClosedByOtherNotice({ proposalId }: { readonly proposalId: string }) {
  const isClosedByOther = useProposalClosedNotice(proposalId);
  return isClosedByOther ? <p className={styles.status}>{CLOSED_TEXT}</p> : null;
}

function failureText(error: unknown, closedCode: string): string | null {
  if (error === null || error === undefined) return null;
  if (isApiError(error) && error.code === closedCode) return CLOSED_TEXT;
  return errorMessage(error);
}

interface ProposalActionsProps {
  readonly apply: UseMutationResult<unknown, Error, void>;
  readonly dismiss: UseMutationResult<unknown, Error, void>;
  /** 主要按鈕的字：「套用」「復原」。 */
  readonly applyLabel: string;
  /** 進行中的字：「套用中…」。 */
  readonly busyLabel: string;
  /** 建議已經處理過時伺服器回的 409 代碼。 */
  readonly closedCode: string;
}

/** 新種類的建議（週期與里程碑、專案、留言、復原項目）共用的「套用」「不要」兩顆按鈕與失敗訊息。 */
export function ProposalActions({ apply, dismiss, applyLabel, busyLabel, closedCode }: ProposalActionsProps) {
  const isWorking = apply.isPending || dismiss.isPending;
  return (
    <>
      <div className={styles.actions}>
        <Button
          variant="primary"
          size="sm"
          aria-disabled={isWorking || undefined}
          onClick={() => {
            if (isWorking) return;
            dismiss.reset();
            apply.mutate();
          }}
        >
          {apply.isPending ? busyLabel : applyLabel}
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
      <Notice tone="danger">{failureText(apply.error ?? dismiss.error, closedCode)}</Notice>
    </>
  );
}
