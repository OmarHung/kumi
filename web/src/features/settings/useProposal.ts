import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { proposeChangeSet } from '../../api/changesets';
import { errorMessage, isApiError } from '../../api/http';
import { queryKeys } from '../../api/queryKeys';
import type { ChangeSetRequest } from '../../api/types';
import { buildChangeSet } from '../../domain/settingsChangeSet';
import { locateIssue, type SettingsIssue } from '../../domain/settingsIssues';
import { summaryError, validateDraft } from '../../domain/settingsValidation';
import { issueBreakdown } from './settingsSections';
import type { EditSession } from './useEditSession';

const CHANGE_SET_INVALID = 'changeset.invalid';
/** 基準版本已經不是目前的版本：別人先套用了新的一版，這份草稿怎麼改都送不出去。 */
const BASE_VERSION_MISMATCH = 'plan.base_version_mismatch';
const SUMMARY_PATH = '/summary';
const OUTDATED_ALERT = '別人已經套用了新版本，這份修改送不出去。請到頁面上方「以最新版本重新開始」。';

/** 按「預覽變更」之後要顯示的東西：每一列的問題、摘要旁的問題、版本是否過時、送出列上方的說明。 */
interface Feedback {
  readonly issues: readonly SettingsIssue[];
  readonly summaryProblem: string | undefined;
  readonly isBaseOutdated: boolean;
  readonly alert: string | null;
}

const NO_FEEDBACK: Feedback = { issues: [], summaryProblem: undefined, isBaseOutdated: false, alert: null };

/**
 * 說明（alert）的文字後面接著問題分布在哪些區塊（「流程 1 個、角色 1 個。」）：問題可能都在目前沒有畫出來的區塊。
 * 伺服器擋下時（changeset.invalid）把每一個問題對到設定頁的那一列；摘要的問題放在摘要旁邊；
 * 版本過時另外說明（不列在「其他問題」）。其他失敗顯示後端的訊息。
 */
function feedbackOf(error: unknown, request: ChangeSetRequest, session: EditSession): Feedback {
  if (!isApiError(error) || error.code !== CHANGE_SET_INVALID) {
    return { ...NO_FEEDBACK, alert: errorMessage(error) };
  }
  const isBaseOutdated = error.details.some((detail) => detail.code === BASE_VERSION_MISMATCH);
  const summary = error.details.find((detail) => detail.path === SUMMARY_PATH);
  const issues = error.details
    .filter((detail) => detail.code !== BASE_VERSION_MISMATCH && detail !== summary)
    .map((detail) => ({ target: locateIssue(detail.path, request, session), message: detail.message }));
  const count = issues.length + (summary === undefined ? 0 : 1);
  const breakdown = issueBreakdown(issues, summary !== undefined);
  const placed = count > 0 ? `伺服器檢查後擋下了這份變更（${String(count)} 個問題），請看標示的地方。${breakdown}。` : errorMessage(error);
  return { issues, summaryProblem: summary?.message, isBaseOutdated, alert: isBaseOutdated ? OUTDATED_ALERT : placed };
}

/** 送出成功時交給設定頁的東西：送出的那一份編輯、摘要與變成的變更集編號（記在記憶體，預覽頁才回得來）。 */
export interface CreatedProposal {
  readonly session: EditSession;
  readonly summary: string;
  readonly changeSetNumber: number;
}

/**
 * 「預覽變更」：先做前端的檢查，通過了才把草稿組成變更集送出（送出時就存成等待核准）。
 * 版本過時時讓專案的內容重抓，設定頁才拿得到新的一版重新開始。
 * created 是剛存好的變更集編號，設定頁看到它就換到預覽頁；onCreated 在那之前拿到送出的是哪一份。
 */
export function useProposal(projectKey: string, session: EditSession, onCreated?: (created: CreatedProposal) => void) {
  const queryClient = useQueryClient();
  const [showErrors, setShowErrors] = useState(false);
  const [feedback, setFeedback] = useState<Feedback>(NO_FEEDBACK);
  const [created, setCreated] = useState<number | null>(null);
  const mutation = useMutation({ mutationFn: (request: ChangeSetRequest) => proposeChangeSet(projectKey, request) });

  function submit(summary: string) {
    const local = validateDraft(session.base, session.draft);
    const summaryProblem = summaryError(summary);
    const count = local.length + (summaryProblem === undefined ? 0 : 1);
    setShowErrors(true);
    if (count > 0) {
      const breakdown = issueBreakdown(local, summaryProblem !== undefined);
      setFeedback({ ...NO_FEEDBACK, summaryProblem, alert: `還有 ${String(count)} 個地方要修正，請看標示的地方。${breakdown}。` });
      return;
    }
    setFeedback(NO_FEEDBACK);
    const request = buildChangeSet(session.base, session.baseVersion, session.draft, summary);
    mutation.mutate(request, {
      onSuccess: (detail) => {
        queryClient.setQueryData(queryKeys.changeSet(projectKey, detail.changeSet.number), detail);
        void queryClient.invalidateQueries({ queryKey: queryKeys.changeSets(projectKey), exact: true });
        onCreated?.({ session, summary, changeSetNumber: detail.changeSet.number });
        setCreated(detail.changeSet.number);
      },
      onError: (error) => {
        const next = feedbackOf(error, request, session);
        setFeedback(next);
        if (next.isBaseOutdated) void queryClient.invalidateQueries({ queryKey: queryKeys.project(projectKey), exact: true });
      },
    });
  }

  /** 捨棄修改、重新開始時一起清掉上一次的問題。 */
  function clear() {
    setShowErrors(false);
    setFeedback(NO_FEEDBACK);
  }

  return {
    submit,
    clear,
    showErrors,
    serverIssues: feedback.issues,
    summaryProblem: feedback.summaryProblem,
    isBaseOutdated: feedback.isBaseOutdated,
    alert: feedback.alert,
    created,
    isPending: mutation.isPending,
  };
}
