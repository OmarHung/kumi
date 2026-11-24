import { useState, type RefObject } from 'react';
import { useReplaceProjectDescription } from '../../api/projectDescription';
import type { ProjectDescription } from '../../api/types';
import { draftKey } from '../../lib/draftStorage';
import { submitDescription, useRefocusAfterClose, useServerBasedDraft } from '../item-page/descriptionDraft';
import { cellErrorMessage } from '../item-table/cells/EditableCell';

/**
 * 專案說明的編輯狀態（draft 是 null 就是沒有在編輯）：草稿存在這個分頁、回到這一頁時還原並進入編輯，
 * 儲存成功（回應已經寫進快取）、取消、沒改就儲存時清掉；結束編輯時焦點回到入口（「編輯」或「開始寫」）。
 * close(saved) 的 saved 是結束之後的說明，決定焦點回到「編輯」還是「開始寫」。草稿的規矩和項目的說明共用（item-page/descriptionDraft.ts）。
 */
export function useProjectDescriptionEditor(
  projectKey: string,
  description: ProjectDescription,
  entryRef: RefObject<HTMLButtonElement | null>,
) {
  const mutation = useReplaceProjectDescription(projectKey);
  const current = description.text;
  const stored = useServerBasedDraft(draftKey('project-description', projectKey), current ?? '');
  const [draft, setDraft] = useState<string | null>(stored.restored);
  const requestRefocus = useRefocusAfterClose(draft, current !== null, entryRef);

  function close(saved: string | null) {
    requestRefocus(saved !== null);
    setDraft(null);
    stored.clear();
    mutation.reset();
  }

  function save() {
    if (draft === null || mutation.isPending) return;
    const send = (next: string | null, done: (saved: string | null) => void) => {
      mutation.mutate(next, { onSuccess: (saved) => { done(saved.text); } });
    };
    submitDescription(draft, current, send, close);
  }

  return {
    draft,
    isSaving: mutation.isPending,
    isRestored: stored.isRestored,
    outdated: stored.outdated,
    error: mutation.isError ? cellErrorMessage(mutation.error, ['text']) : null,
    /** initial：目前的說明，或開始寫時選的格式的內容。 */
    start: (initial: string) => {
      mutation.reset();
      stored.rebase();
      setDraft(initial);
    },
    change: (next: string) => {
      setDraft(next);
      stored.save(next);
      if (mutation.isError) mutation.reset();
    },
    // 捨棄還原的草稿：回到伺服器上的內容，留在編輯中。
    discard: () => {
      setDraft(current ?? '');
      stored.rebase();
      stored.clear();
    },
    save,
    cancel: () => {
      if (!mutation.isPending) close(current);
    },
  };
}

export type ProjectDescriptionEditor = ReturnType<typeof useProjectDescriptionEditor>;
