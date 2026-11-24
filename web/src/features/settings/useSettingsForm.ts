import { useEffect, useState, type RefObject } from 'react';
import { useNavigate } from 'react-router';
import { errorMessage } from '../../api/http';
import type { ProjectDetail } from '../../api/types';
import { scrollToPageTop } from '../../lib/pageTop';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { changeSetPagePath, type SettingsSection } from '../work-items/itemLinks';
import { useDraftMemory } from './useDraftMemory';
import { useEditSession } from './useEditSession';
import { useLeaveGuard, type LeaveGuardOptions } from './useLeaveGuard';
import { useProposal } from './useProposal';

/** 焦點的去處：頁面的標題，與版本過時的說明。 */
export interface SettingsFocusRefs {
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
  readonly outdatedRef: RefObject<HTMLDivElement | null>;
}

/** 離開提醒要知道的頁面狀態：有沒有對話框開著，確認換區塊之後焦點放哪裡。 */
export type SettingsGuardState = Pick<LeaveGuardOptions, 'hasOpenDialog' | 'onSwitchSection'>;

/** 送出之後：成功就換到預覽頁；伺服器才說版本過時時，焦點移到說明（「以最新版本重新開始」就在裡面）。 */
function useAfterSubmit(proposal: ReturnType<typeof useProposal>, projectKey: string, outdatedRef: SettingsFocusRefs['outdatedRef']) {
  const navigate = useNavigate();
  useEffect(() => {
    if (proposal.created !== null) void navigate(changeSetPagePath(projectKey, proposal.created));
  }, [proposal.created, navigate, projectKey]);
  useEffect(() => {
    if (proposal.isBaseOutdated) outdatedRef.current?.focus();
  }, [proposal.isBaseOutdated, outdatedRef]);
}

/**
 * 設定頁的狀態：草稿（useEditSession）、送出（useProposal）、摘要、離開提醒，與兩種重新開始。
 * 「捨棄修改」與「以最新版本重新開始」之後，按的按鈕跟著不見：焦點移到頁面的標題，畫面捲回最上面。
 * 預覽頁「回去修改」帶回來的草稿與摘要（useDraftMemory）在這裡接上；section 是目前的區塊，送出時記下，回來時回到這裡。
 */
export function useSettingsForm(
  detail: ProjectDetail,
  section: SettingsSection,
  { headingRef, outdatedRef }: SettingsFocusRefs,
  guard: SettingsGuardState,
) {
  const projectKey = detail.project.key;
  const canEdit = detail.myPermissions.includes('blueprint.propose');
  const memory = useDraftMemory(projectKey, section, canEdit);
  const editing = useEditSession(detail, memory.resumed?.session);
  const proposal = useProposal(projectKey, editing.session, memory.remember);
  const [summary, setSummary] = useState(memory.resumed?.summary ?? '');
  const [restartError, setRestartError] = useState<string | null>(null);
  // 送出成功後不再擋：接著要換到預覽頁。blocker 要先於換頁的更新，所以放在 useAfterSubmit 前面。
  const leaveDialog = useLeaveGuard({ ...guard, hasChanges: canEdit && editing.isDirty && proposal.created === null });
  useAfterSubmit(proposal, projectKey, outdatedRef);

  const startOver = (reset: () => void) => {
    updateThenFocus(() => {
      reset();
      proposal.clear();
      setSummary('');
      setRestartError(null);
    }, [headingRef]);
    // 標題在黏住的頁列上，焦點過去不會捲動：使用者按的按鈕可能在頁面很下面（送出列）。
    scrollToPageTop();
  };
  async function restart() {
    try {
      const latest = await editing.latestDetail();
      startOver(() => { editing.reset(latest); });
    } catch (error) {
      setRestartError(errorMessage(error));
    }
  }

  return {
    editing,
    proposal,
    summary,
    setSummary,
    canEdit,
    leaveDialog,
    restartError,
    isOutdated: editing.isOutdated || proposal.isBaseOutdated,
    discard: () => {
      memory.forget();
      startOver(() => { editing.reset(); });
    },
    restart,
  };
}
