import type { UseMutationResult } from '@tanstack/react-query';
import { useId } from 'react';
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

export function failureText(error: unknown, closedCode: string): string | null {
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
  /** 還不能套用（卡片輸入沒填好）：按鈕標成 aria-disabled，原因由 applyBlockedReason 說。 */
  readonly isApplyBlocked?: boolean;
  /** 只給報讀軟體的原因（aria-describedby）；isApplyBlocked 為真時才用。 */
  readonly applyBlockedReason?: string;
}

/** 新種類的建議（週期與里程碑、專案、留言、復原項目）共用的「套用」「不要」兩顆按鈕與失敗訊息。 */
export function ProposalActions({ apply, dismiss, applyLabel, busyLabel, closedCode, isApplyBlocked = false, applyBlockedReason }: ProposalActionsProps) {
  const isWorking = apply.isPending || dismiss.isPending;
  const isBlocked = isWorking || isApplyBlocked;
  const reasonId = useId();
  const hasReason = isApplyBlocked && applyBlockedReason !== undefined;
  return (
    <>
      <div className={styles.actions}>
        <Button
          variant="primary"
          size="sm"
          aria-disabled={isBlocked || undefined}
          aria-describedby={hasReason ? reasonId : undefined}
          onClick={() => {
            if (isBlocked) return;
            dismiss.reset();
            apply.mutate();
          }}
        >
          {apply.isPending ? busyLabel : applyLabel}
        </Button>
        {hasReason && (
          <span id={reasonId} className="visually-hidden">
            {applyBlockedReason}
          </span>
        )}
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
