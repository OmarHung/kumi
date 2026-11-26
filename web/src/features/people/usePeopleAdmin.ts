import { useState } from 'react';
import { errorMessage } from '../../api/http';
import type { Person } from '../../api/types';
import type { PersonActionHandlers } from './PersonActions';
import { useSetPersonDisabled } from './personMutations';

type PersonDialogKind = 'disable' | 'resetPassword' | 'changeRole' | 'editProfile';

export type PersonDialog = { readonly kind: PersonDialogKind; readonly person: Person } | null;

interface Feedback {
  readonly clear: () => void;
  readonly succeed: (text: string) => void;
  readonly fail: (text: string) => void;
}

function without(ids: ReadonlySet<string>, personId: string): ReadonlySet<string> {
  return new Set([...ids].filter((id) => id !== personId));
}

/**
 * 「重新啟用帳號」直接送出（可以再停用回去，不必確認）。可以連續對好幾列按：每一次各自等結果（mutateAsync），
 * 共用一個 mutation 的 mutate 只會叫最後一次的 onSuccess，前面那幾個人的結果會不見。
 * 送出中的人記在 pendingIds：那一列寫「啟用中…」、選單打不開。一批裡第一個送出時才清掉上一個結果。
 */
function useEnablePerson(feedback: Feedback) {
  const enabling = useSetPersonDisabled();
  const [pendingIds, setPendingIds] = useState<ReadonlySet<string>>(() => new Set());

  function enable(person: Person) {
    if (pendingIds.size === 0) feedback.clear();
    setPendingIds((ids) => new Set([...ids, person.personId]));
    void enabling
      .mutateAsync({ personId: person.personId, disabled: false })
      .then(
        () => {
          feedback.succeed(`已重新啟用「${person.displayName}」的帳號。`);
        },
        (error: unknown) => {
          feedback.fail(`沒有重新啟用「${person.displayName}」的帳號：${errorMessage(error)}`);
        },
      )
      .finally(() => {
        setPendingIds((ids) => without(ids, person.personId));
      });
  }

  return { enable, pendingIds };
}

/**
 * 人員頁上方的訊息與每一列的動作：新增、變更角色、停用、重新啟用、重設密碼的結果都寫在同一則 status 裡（一直掛著，
 * 晚一點才填的內容報讀軟體才唸得到）；直接送出的重新啟用失敗時寫在 alert 裡（對話框的失敗寫在對話框裡）。
 * 開始下一個動作時先清掉上一個結果，留著的話看起來像這一次的；同時送出的幾個重新啟用，結果一句一句接在後面。
 */
export function usePeopleAdmin() {
  const [dialog, setDialog] = useState<PersonDialog>(null);
  const [messages, setMessages] = useState<readonly string[]>([]);
  const [failures, setFailures] = useState<readonly string[]>([]);
  const clear = () => {
    setMessages([]);
    setFailures([]);
  };
  const { enable, pendingIds } = useEnablePerson({
    clear,
    succeed: (text) => {
      setMessages((current) => [...current, text]);
    },
    fail: (text) => {
      setFailures((current) => [...current, text]);
    },
  });
  const openDialog = (kind: PersonDialogKind) => (person: Person) => {
    clear();
    setDialog({ kind, person });
  };
  const handlers: PersonActionHandlers = {
    onEditProfile: openDialog('editProfile'),
    onChangeRole: openDialog('changeRole'),
    onResetPassword: openDialog('resetPassword'),
    onDisable: openDialog('disable'),
    onEnable: enable,
    pendingPersonIds: pendingIds,
  };
  return {
    dialog,
    closeDialog: () => {
      setDialog(null);
    },
    message: messages.length === 0 ? null : messages.join(' '),
    failure: failures.length === 0 ? null : failures.join(' '),
    clear,
    /** 對話框做完了（或新增好了）：關上對話框，結果寫在頁面上方。 */
    finish: (text: string) => {
      setDialog(null);
      setFailures([]);
      setMessages([text]);
    },
    handlers,
  };
}

export type PeopleAdmin = ReturnType<typeof usePeopleAdmin>;
