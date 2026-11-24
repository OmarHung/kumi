import { useEffect, useRef, useState, type RefObject } from 'react';
import type { Activity } from '../../api/types';
import { draftKey, readDraft, removeDraft } from '../../lib/draftStorage';
import { isFocusLost } from '../../lib/updateThenFocus';
import { useCommentAbilities, type CommentAbilities } from './commentAbilities';
import { isEditDraftWorthKeeping } from './editDraft';

/** 編輯的狀態：restored 是重新整理、換頁回來時因為有沒儲存的草稿而開著，不搶焦點。 */
export type CommentEditMode = 'view' | 'editing' | 'restored';

/** 結束編輯的原因：saved 與 cancelled 把焦點放回「更多」；deleted 是儲存時發現已經被刪除了。 */
export type EditFinish = 'saved' | 'cancelled' | 'deleted';

/** 焦點的去處：整包傳會讓 lint 以為在 render 裡讀 ref，所以和 control 分開攤開傳。 */
export interface CommentEntryRefs {
  /** 「更多」按鈕；編輯結束後焦點放回這裡。 */
  readonly triggerRef: RefObject<HTMLButtonElement | null>;
  /** 留言的卡片（內文或佔位）；刪除、復原之後焦點放到這裡。 */
  readonly bodyRef: RefObject<HTMLDivElement | null>;
}

export interface CommentEntryControl {
  readonly abilities: CommentAbilities;
  readonly mode: CommentEditMode;
  /** 儲存時發現這則留言已經被刪除了：先說一聲，重抓回來之後就是佔位。 */
  readonly vanishedNotice: string | null;
  readonly startEditing: () => void;
  readonly finishEditing: (reason: EditFinish) => void;
  /** 刪除、復原成功之後：等活動換成新的樣子，就把焦點放到卡片上。 */
  readonly requestBodyFocus: () => void;
  /** 自己刪除成功：編輯狀態降回檢視、清掉沒儲存的草稿（復原之後編輯器不會自己打開）。 */
  readonly discardEdit: () => void;
}

/** 這則留言有沒有沒儲存的編輯草稿（只有真的能編輯的人才還原）。 */
function hasEditDraft(itemKey: string, activity: Activity, abilities: CommentAbilities): boolean {
  if (!abilities.canEdit) return false;
  const stored = readDraft(draftKey('comment-edit', itemKey, activity.id));
  return stored !== null && isEditDraftWorthKeeping(activity.comment ?? '', stored);
}

/**
 * 一則留言的狀態：編輯中與否、焦點的去處。放在 ActivityEntry 這一層（以活動編號為 key，活動重抓、即時更新時不會重新掛上），
 * 編輯中的狀態因此不跟著物件換：重抓換了新的活動物件，編輯器也不會被關掉、內容不會被蓋掉。
 */
export function useCommentEntry(itemKey: string, activity: Activity): { readonly control: CommentEntryControl; readonly refs: CommentEntryRefs } {
  const abilities = useCommentAbilities(activity);
  const [mode, setMode] = useState<CommentEditMode>(() => (hasEditDraft(itemKey, activity, abilities) ? 'restored' : 'view'));
  const [vanishedNotice, setVanishedNotice] = useState<string | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const bodyRef = useRef<HTMLDivElement>(null);
  const wantsBodyFocus = useRef(false);
  const isDeleted = activity.deletedAt !== null;
  // 被別人刪掉（即時更新進來）：編輯狀態降回檢視，草稿留著（沒存的字不能丟）；之後復原，編輯器不自己打開，
  // 使用者再按「編輯」時才還原草稿並提示。同時清掉「已經被刪除了」的提示，復原之後不要再冒出來。
  if (isDeleted && mode !== 'view') setMode('view');
  if (isDeleted && vanishedNotice !== null) setVanishedNotice(null);
  // 沒有編輯的權限了（權限剛被拿掉、封存），或留言被刪了：不顯示編輯器（草稿還在分頁裡，復原之後仍然找得到）。
  const shownMode = abilities.canEdit && !isDeleted ? mode : 'view';

  // 刪除、復原成功之後活動換成新的樣子（isDeleted 變了）才放焦點：換之前的卡片馬上就會被換掉。
  // 別人刪掉的（沒有 requestBodyFocus）：焦點原本在已經消失的確認框或編輯器上、掉到頁面本體時，也接到佔位上。
  const wasDeleted = useRef(isDeleted);
  useEffect(() => {
    const isNewlyDeleted = isDeleted && !wasDeleted.current;
    wasDeleted.current = isDeleted;
    if (!wantsBodyFocus.current && !(isNewlyDeleted && isFocusLost())) return;
    wantsBodyFocus.current = false;
    const body = bodyRef.current;
    if (body === null) return;
    body.tabIndex = -1;
    body.focus();
  }, [isDeleted]);

  const control: CommentEntryControl = {
    abilities,
    mode: shownMode,
    vanishedNotice,
    startEditing: () => {
      setVanishedNotice(null);
      setMode('editing');
    },
    finishEditing: (reason) => {
      setMode('view');
      if (reason === 'deleted') {
        setVanishedNotice('這則留言已經被刪除了。');
        // 重抓回來變成佔位時，焦點再接到佔位上（提示會跟著不見）。
        wantsBodyFocus.current = true;
      } else triggerRef.current?.focus();
    },
    requestBodyFocus: () => {
      wantsBodyFocus.current = true;
    },
    discardEdit: () => {
      removeDraft(draftKey('comment-edit', itemKey, activity.id));
      setMode('view');
    },
  };
  return { control, refs: { triggerRef, bodyRef } };
}
