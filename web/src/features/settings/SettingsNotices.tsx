import { useQueryClient } from '@tanstack/react-query';
import type { Ref } from 'react';
import { Link } from 'react-router';
import { useChangeSets } from '../../api/queries';
import type { ChangeSet } from '../../api/types';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import type { ArrivalState } from '../../lib/useArrivalNotice';
import { changeSetPagePath, changeSetsPath } from '../work-items/itemLinks';
import { IssueText } from './RowParts';
import { hasProposedDraft } from './settingsDraftMemory';
import styles from './Settings.module.css';

/** 套用變更集之後導回設定頁時帶的提示（navigate 的 state）。 */
export type SettingsLocationState = ArrivalState;

/** 一直掛著的狀態列：沒有提示時是空的、看不見（Notice 照樣掛著同一個元素，晚一點填進來的內容才唸得到）。 */
export function AppliedNotice({ message }: { readonly message: string }) {
  return (
    <Notice tone="success" role="status">
      {message}
    </Notice>
  );
}

/**
 * 這個分頁剛從設定頁送出、還在等待核准的那一份（草稿還在記憶體裡）：用上一頁從預覽頁回來時，設定頁是目前的設定，
 * 要告訴使用者修改沒有不見——到預覽頁按「回去修改」就帶得回來。
 */
function useMyProposedChangeSet(projectKey: string, pending: readonly ChangeSet[]): ChangeSet | undefined {
  const queryClient = useQueryClient();
  const { personId } = useCurrentUser();
  return pending.find(
    (changeSet) =>
      changeSet.proposedBy.personId === personId &&
      hasProposedDraft(queryClient, projectKey, changeSet.baseVersion, changeSet.number, personId),
  );
}

/**
 * 有幾份變更集在等核准；讀不到清單時不顯示（不影響編輯）。一開始就在、不必唸：role="none"。
 * 其中有自己剛送出的那一份時多一句，連到它的預覽頁（用同一份清單，不多打請求）。
 */
export function PendingNotice({ projectKey }: { readonly projectKey: string }) {
  const changeSets = useChangeSets(projectKey);
  const pending = changeSets.data?.filter((changeSet) => changeSet.status === 'pending') ?? [];
  const mine = useMyProposedChangeSet(projectKey, pending);
  if (pending.length === 0) {
    return null;
  }
  return (
    <Notice tone="info" role="none">
      <p>
        <Link to={changeSetsPath(projectKey)} className={styles.noticeLink}>{`有 ${String(pending.length)} 份變更等待核准`}</Link>
        ：套用其中一份之後，其他以同一版為基準的會過期。
      </p>
      {mine !== undefined && (
        <p>
          你剛送出的
          <Link to={changeSetPagePath(projectKey, mine.number)} className={styles.noticeLink}>{`變更集 #${String(mine.number)}`}</Link>
          {' 還在等待核准，可以到預覽頁按「回去修改」帶著修改回來。'}
        </p>
      )}
    </Notice>
  );
}

interface OutdatedNoticeProps {
  readonly baseVersion: number;
  /** 手上的專案內容是第幾版；送出時才知道過時的話，重抓回來之前還是舊的版本。 */
  readonly latestVersion: number;
  /** 讀不到最新的設定時的說明。 */
  readonly error: string | null;
  readonly onRestart: () => void;
  readonly ref?: Ref<HTMLDivElement>;
}

/**
 * 別人套用了新的一版（編輯中重抓時看到，或送出時伺服器說基準版本過時）：這份草稿送出一定會被擋下，
 * 說明並讓使用者以最新版本重新開始。送出時才知道的，焦點會移到這裡（提示本身，tabIndex={-1}）。
 */
export function OutdatedNotice({ baseVersion, latestVersion, error, onRestart, ref }: OutdatedNoticeProps) {
  const latest = latestVersion > baseVersion ? `設定在你編輯時已經更新為第 ${String(latestVersion)} 版，` : '';
  const restart = (
    <Button size="sm" onClick={onRestart}>
      以最新版本重新開始
    </Button>
  );
  return (
    <Notice ref={ref} tabIndex={-1} tone="warning" role="alert" actions={restart}>
      <p>{`別人已經套用了新版本：${latest}你的修改是以第 ${String(baseVersion)} 版為準，送不出去。請以最新版本重新開始，你的修改會捨棄。`}</p>
      {error !== null && <IssueText>{error}</IssueText>}
    </Notice>
  );
}
