import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { ProjectBlueprint } from '../../api/blueprint';
import { getProject } from '../../api/projects';
import { queryKeys } from '../../api/queryKeys';
import type { ProjectDetail } from '../../api/types';
import { draftFromBlueprint, type SettingsDraft } from '../../domain/settingsDraft';
import { pendingChangeCount } from '../../domain/settingsStatus';

/** 一次編輯：以哪一版的藍圖為基準，以及目前的草稿。送出的變更集用這個版本當 baseVersion。 */
export interface EditSession {
  readonly base: ProjectBlueprint;
  readonly baseVersion: number;
  readonly draft: SettingsDraft;
}

function startSession(detail: ProjectDetail): EditSession {
  return { base: detail.blueprint, baseVersion: detail.project.blueprintVersion, draft: draftFromBlueprint(detail.blueprint) };
}

/**
 * 設定頁的草稿。專案的藍圖換了新的一版（別人套用了變更集、或剛套用完回到這一頁）時：
 * 還沒有修改就直接換成最新的；已經有修改就留著，標成「過時」，由使用者決定捨棄後重新開始。
 * 版本只會往前：以最新版本重新開始時，手上的專案內容可能還是舊的（快取的通知晚一步到），不能因此退回舊的一版。
 * resumed 是從預覽頁「回去修改」帶回來的整份編輯：連同它自己的基準藍圖與版本一起接回來，不套在手上的藍圖上——
 * 這段期間別人套用了新版本的話，照上面的規則標成過時。
 */
export function useEditSession(detail: ProjectDetail, resumed?: EditSession) {
  const queryClient = useQueryClient();
  const [session, setSession] = useState(() => resumed ?? startSession(detail));
  const count = pendingChangeCount(session.base, session.draft);
  const latestVersion = detail.project.blueprintVersion;
  if (latestVersion > session.baseVersion && count === 0) {
    setSession(startSession(detail));
  }

  function update(change: (draft: SettingsDraft) => SettingsDraft) {
    setSession((current) => ({ ...current, draft: change(current.draft) }));
  }

  /** 丟掉草稿，從 next（預設是手上的專案內容）重新開始。 */
  function reset(next: ProjectDetail = detail) {
    setSession(startSession(next));
  }

  /** 向伺服器讀專案最新的內容（也寫進快取）：送出時才知道版本過時，手上的那一份可能還沒重抓完。 */
  function latestDetail(): Promise<ProjectDetail> {
    const projectKey = detail.project.key;
    return queryClient.query({
      queryKey: queryKeys.project(projectKey),
      queryFn: ({ signal }) => getProject(projectKey, signal),
      staleTime: 0,
    });
  }

  return {
    session,
    count,
    isDirty: count > 0,
    isOutdated: latestVersion > session.baseVersion,
    latestVersion,
    update,
    reset,
    latestDetail,
  };
}
