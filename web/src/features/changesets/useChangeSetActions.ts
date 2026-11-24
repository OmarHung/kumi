import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { applyChangeSet, rejectChangeSet } from '../../api/changesets';
import { errorMessage, isApiError, type ApiError } from '../../api/http';
import { queryKeys } from '../../api/queryKeys';
import type { ChangeSetDetail } from '../../api/types';
import { DEFAULT_SETTINGS_SECTION, settingsSectionPath } from '../work-items/itemLinks';
import { forgetChangeSetDraft } from '../settings/settingsDraftMemory';
import type { SettingsLocationState } from '../settings/SettingsNotices';

/** 套用或放棄被擋下、需要重新讀這份變更集的情況：顯示在頁面上，不是只在對話框裡。 */
export interface ActionProblem {
  readonly message: string;
  readonly reasons: readonly string[];
  /** 要重新編輯時給一個到設定頁的連結。 */
  readonly suggestsEditing: boolean;
}

const PAGE_LEVEL_CODES = new Set(['changeset.outdated', 'changeset.apply_blocked', 'changeset.not_pending']);

// 等不到鎖是暫時的：留在對話框裡說明，按鈕不停用，等一下再按就好。伺服器的訊息是給開發者看的，換成固定的說法。
const BUSY_CODE = 'changeset.busy';
const BUSY_MESSAGE = '專案正在被修改，請稍後再試。';

function dialogError(error: unknown): string | null {
  if (!isApiError(error)) return errorMessage(error);
  if (PAGE_LEVEL_CODES.has(error.code)) return null;
  return error.code === BUSY_CODE ? BUSY_MESSAGE : error.message;
}

function problemOf(error: ApiError): ActionProblem {
  switch (error.code) {
    case 'changeset.outdated':
      return { message: '別的變更已經先套用，這份變更集已經過期，請到設定頁重新編輯。', reasons: [], suggestsEditing: true };
    case 'changeset.apply_blocked':
      return { message: '現在的資料讓這份變更集套用不了：', reasons: error.details.map((detail) => detail.message), suggestsEditing: false };
    default:
      return { message: '這份變更集已經不是等待核准，可能已經被別人套用或放棄了。', reasons: [], suggestsEditing: false };
  }
}

/**
 * 套用與放棄。套用成功後藍圖與項目都變了：專案（內容、項目清單、看板、變更集）、每個項目頁、我的工作、專案清單都要失效，
 * 再回到設定頁並說明是第幾版。過時、被資料擋住、已經不是等待核准時，關上對話框、在頁面上說明，並重新讀這份變更集。
 * 套用或放棄之後，設定頁送出這份時記下的草稿（「回去修改」用的）不再需要，一起清掉。
 */
export function useChangeSetActions(projectKey: string, number: number) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [problem, setProblem] = useState<ActionProblem | null>(null);
  const [notice, setNotice] = useState('');

  function refreshAfterProblem(error: unknown): boolean {
    if (!isApiError(error) || !PAGE_LEVEL_CODES.has(error.code)) return false;
    setProblem(problemOf(error));
    void queryClient.invalidateQueries({ queryKey: queryKeys.changeSets(projectKey) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.project(projectKey), exact: true });
    return true;
  }

  const apply = useMutation({
    mutationFn: () => applyChangeSet(projectKey, number),
    onSuccess: async (result: ChangeSetDetail) => {
      queryClient.setQueryData(queryKeys.changeSet(projectKey, number), result);
      forgetChangeSetDraft(queryClient, projectKey, result.changeSet.baseVersion, number);
      void queryClient.invalidateQueries({ queryKey: ['item'] });
      void queryClient.invalidateQueries({ queryKey: queryKeys.myWork });
      void queryClient.invalidateQueries({ queryKey: queryKeys.projects });
      // 專案的內容要先讀回新的一版，設定頁才會從新的藍圖開始。
      await queryClient.invalidateQueries({ queryKey: queryKeys.project(projectKey) });
      const version = result.changeSet.appliedVersion;
      const state: SettingsLocationState = {
        notice: version === null ? `已套用變更集 #${String(number)}。` : `已套用變更集 #${String(number)}，藍圖是第 ${String(version)} 版。`,
      };
      // 直接到預設的區塊（不經過設定頁本身的轉址）：提示落在那裡，只唸一次。
      await navigate(settingsSectionPath(projectKey, DEFAULT_SETTINGS_SECTION), { state });
    },
  });

  const reject = useMutation({
    mutationFn: () => rejectChangeSet(projectKey, number),
    onSuccess: (result: ChangeSetDetail) => {
      queryClient.setQueryData(queryKeys.changeSet(projectKey, number), result);
      forgetChangeSetDraft(queryClient, projectKey, result.changeSet.baseVersion, number);
      void queryClient.invalidateQueries({ queryKey: queryKeys.changeSets(projectKey), exact: true });
      setNotice(`已放棄變更集 #${String(number)}，設定沒有改變。`);
    },
  });

  return {
    apply,
    reject,
    problem,
    notice,
    /** 對話框裡顯示的失敗訊息（等不到鎖時說稍後再試）；頁面層級的問題（過時、被擋住）另外處理，這裡回 null。 */
    dialogError,
    refreshAfterProblem,
    /** 沒有對話框可以放的失敗（「回去修改」放棄失敗）：直接在頁面上說明。 */
    showProblem: setProblem,
    clearProblem: () => { setProblem(null); },
  };
}
