import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useRef } from 'react';
import { useNavigate } from 'react-router';
import { rejectChangeSet } from '../../api/changesets';
import { errorMessage } from '../../api/http';
import { getProject } from '../../api/projects';
import { queryKeys } from '../../api/queryKeys';
import type { ChangeSet, ChangeSetDetail, ProjectDetail } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { DEFAULT_SETTINGS_SECTION, settingsSectionPath } from '../work-items/itemLinks';
import { hasProposedDraft, releaseProposedDraft } from '../settings/settingsDraftMemory';
import type { SettingsLocationState } from '../settings/SettingsNotices';
import type { useChangeSetActions } from './useChangeSetActions';

type Actions = ReturnType<typeof useChangeSetActions>;

const REVISE_FAILED = '放棄這份變更集失敗了，還不能回去修改；你的修改都還在。';

/** 設定頁的換頁提示。草稿的基準版本已經不是最新的時候，設定頁上方會說送不出去：這裡不能同時說「可以繼續改」。 */
function arrivalNotice(number: number, hasDraft: boolean, isOutdated: boolean): SettingsLocationState {
  const rejected = `已放棄變更集 #${String(number)}`;
  if (!hasDraft) return { notice: `${rejected}，設定沒有改變。` };
  if (isOutdated) return { notice: `${rejected}。修改還在，但別人已經套用了新版本，要先以最新版本重新開始才送得出去。` };
  return { notice: `${rejected}，修改還在，可以繼續改。` };
}

/**
 * 回到設定頁之前讀一次專案最新的內容（也寫進快取）：這段期間別人套用了新版本的話，換頁提示照實說，
 * 設定頁一掛上就是過時的說明，不會先說「可以繼續改」、重抓完才改口。讀不到就用手上的那一份。
 */
async function latestVersion(queryClient: QueryClient, projectKey: string): Promise<number | null> {
  try {
    const detail = await queryClient.query({
      queryKey: queryKeys.project(projectKey),
      queryFn: ({ signal }) => getProject(projectKey, signal),
      staleTime: 0,
    });
    return detail.project.blueprintVersion;
  } catch {
    return queryClient.getQueryData<ProjectDetail>(queryKeys.project(projectKey))?.project.blueprintVersion ?? null;
  }
}

/**
 * 預覽頁的「回去修改」：只有等待核准、而且是這個人剛從設定頁送出（草稿還在記憶體裡）的那一份才有。
 * 先放棄這份變更集（否則每預覽一次就多一份等待核准的；交接文件第 8 節第 11 題），成功了才帶著草稿回到送出時的區塊。
 * 回去之後不在這一頁另外說「已放棄」（使用者已經不在這裡），由設定頁的換頁提示說修改還在。
 * 放棄失敗就留在這一頁說明原因，草稿不動：過時、已經不是等待核准的照套用的做法重新讀這份變更集，其他的直接說明。
 * 送出中按鈕停得到（aria-disabled，焦點不會掉到頁面本體），再按不重送。
 */
export function useReviseChangeSet(projectKey: string, changeSet: ChangeSet, actions: Actions) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { personId } = useCurrentUser();
  const { number, baseVersion } = changeSet;
  const isSending = useRef(false);
  const canRevise =
    changeSet.status === 'pending' &&
    changeSet.proposedBy.personId === personId &&
    hasProposedDraft(queryClient, projectKey, baseVersion, number, personId);
  const mutation = useMutation({
    mutationFn: () => rejectChangeSet(projectKey, number),
    onSuccess: async (result: ChangeSetDetail) => {
      const latest = await latestVersion(queryClient, projectKey);
      const section = releaseProposedDraft(queryClient, projectKey, result.changeSet.baseVersion, number);
      const state = arrivalNotice(number, section !== null, latest !== null && latest > result.changeSet.baseVersion);
      // 先換頁再更新這份變更集：先更新的話，這一頁會先畫一次「已放棄」，按的按鈕跟著不見、焦點掉到頁面本體。
      await navigate(settingsSectionPath(projectKey, section ?? DEFAULT_SETTINGS_SECTION), { state });
      queryClient.setQueryData(queryKeys.changeSet(projectKey, number), result);
      void queryClient.invalidateQueries({ queryKey: queryKeys.changeSets(projectKey), exact: true });
    },
    onError: (error) => {
      if (actions.refreshAfterProblem(error)) return;
      const reason = actions.dialogError(error) ?? errorMessage(error);
      actions.showProblem({ message: `${REVISE_FAILED}${reason}`, reasons: [], suggestsEditing: false });
    },
  });
  return {
    canRevise,
    isPending: mutation.isPending,
    revise: () => {
      if (isSending.current) return;
      isSending.current = true;
      actions.clearProblem();
      mutation.mutate(undefined, {
        onError: () => {
          isSending.current = false;
        },
      });
    },
  };
}
