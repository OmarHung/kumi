import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useCurrentUser } from '../../auth/CurrentUser';
import type { SettingsSection } from '../work-items/itemLinks';
import { draftToResume, forgetDraft, forgetProjectDrafts, rememberProposedDraft } from './settingsDraftMemory';
import type { CreatedProposal } from './useProposal';

/**
 * 設定頁和記憶體裡的草稿（settingsDraftMemory.ts）：掛上時接回預覽頁「回去修改」帶回來的那一份——接到就從記憶體拿掉，
 * 之後捨棄或離開設定頁、再進來就是目前的設定；送出成功時記下送出的那一份與當時的區塊；「捨棄修改」時清掉這個專案記下的。
 * 唯讀的人不接回草稿（改不了，也送不出去）。
 */
export function useDraftMemory(projectKey: string, section: SettingsSection, canEdit: boolean) {
  const queryClient = useQueryClient();
  const { personId } = useCurrentUser();
  const [resumed] = useState(() => (canEdit ? draftToResume(queryClient, projectKey, personId) : undefined));
  useEffect(() => {
    if (resumed !== undefined) forgetDraft(queryClient, resumed);
  }, [queryClient, resumed]);
  return {
    resumed,
    remember: ({ session, summary, changeSetNumber }: CreatedProposal) => {
      rememberProposedDraft(queryClient, { personId, projectKey, session, summary, section }, changeSetNumber);
    },
    forget: () => {
      forgetProjectDrafts(queryClient, projectKey);
    },
  };
}
