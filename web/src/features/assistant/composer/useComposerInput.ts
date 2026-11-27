import { useCallback, useLayoutEffect, useMemo, useState, type KeyboardEvent, type RefObject } from 'react';
import type { AssistantScope } from '../../../api/assistant';
import { useProjectDetails, usePeople } from '../../../api/queries';
import type { Member } from '../../../api/types';
import { useCurrentUser } from '../../../auth/CurrentUser';
import { isCommitEnter } from '../../../lib/keyboard';
import { useMentionInput } from '../../item-page/mentions/useMentionInput';
import { isSendKeystroke, type SendKey } from '../sendKey';
import { applyMentions, plainMentionInsert, type MentionPick } from './composerMentions';
import { continueList } from './listContinuation';

/** 輸入框的字數沒有另外的上限（伺服器收訊息時才檢查）；提及只需要一個放得下的數字。 */
const NO_LIMIT = Number.MAX_SAFE_INTEGER;
const NO_PROJECT_KEYS: readonly string[] = [];

/**
 * 打 @ 時可以選的人：專案對話是專案的成員，組織對話是組織裡沒停用的人（訪客讀不到人員清單，沒有候選就沒有清單）。
 * 還沒讀到時是 undefined（功能先不開）。
 */
function useCandidates(projectKey: AssistantScope): readonly Member[] | undefined {
  const { role } = useCurrentUser();
  const details = useProjectDetails(projectKey === null ? NO_PROJECT_KEYS : [projectKey]);
  const people = usePeople(projectKey === null && role !== 'guest');
  const projectMembers = projectKey === null ? undefined : details.get(projectKey)?.members;
  const organizationPeople = people.data;
  return useMemo(() => {
    if (projectKey !== null) return projectMembers;
    return organizationPeople?.filter((person) => !person.disabled).map((person) => ({ personId: person.personId, displayName: person.displayName, role: person.role }));
  }, [projectKey, projectMembers, organizationPeople]);
}

/**
 * 輸入框的提及：清單與鍵盤照項目留言的那一套，選了只寫 @名稱，記住這次選過的人；
 * resolve 在送出時把文字裡還在的 @名稱 換成 @[名稱](mention:編號)，clear 在伺服器收下之後忘掉選過的人。
 * 送出失敗、字還在輸入框時，選過的人也還在，再送一次照樣換得出來。
 */
export function useComposerMentions(projectKey: AssistantScope, text: string, setText: (text: string) => void) {
  const members = useCandidates(projectKey);
  const [picks, setPicks] = useState<readonly MentionPick[]>([]);
  const onChosen = useCallback((member: Member) => {
    setPicks((current) => [...current, { personId: member.personId, displayName: member.displayName }]);
  }, []);
  const mention = useMentionInput({ value: text, onChange: setText, members, maxLength: NO_LIMIT, insert: plainMentionInsert, onChosen });
  return {
    mention,
    resolve: (value: string) => applyMentions(value, picks),
    clear: () => {
      setPicks([]);
    },
  };
}

/**
 * 「換行」的那個按鍵（和送出相反的那一個：Enter 送出時是 Shift+Enter，⌘／Ctrl+Enter 送出時是 Enter）。
 * 輸入法選字用的 Enter 不算。
 */
function isNewlineKeystroke(event: KeyboardEvent, sendKey: SendKey): boolean {
  return isCommitEnter(event) && !isSendKeystroke(event, sendKey);
}

/** 清單接續（continueList）：改完文字之後把游標放到新的位置，要等文字框畫好才放得上去。 */
export function useListContinuation(inputRef: RefObject<HTMLTextAreaElement | null>, onChange: (text: string) => void) {
  const [placement, setPlacement] = useState<{ readonly caret: number } | null>(null);
  useLayoutEffect(() => {
    if (placement !== null) inputRef.current?.setSelectionRange(placement.caret, placement.caret);
  }, [inputRef, placement]);
  /** 回傳 true 表示這個按鍵接續了清單，呼叫端不要再處理。 */
  return (event: KeyboardEvent<HTMLTextAreaElement>, sendKey: SendKey): boolean => {
    if (!isNewlineKeystroke(event, sendKey)) return false;
    const { value, selectionStart, selectionEnd } = event.currentTarget;
    const continued = continueList(value, selectionStart, selectionEnd);
    if (continued === null) return false;
    event.preventDefault();
    onChange(continued.value);
    setPlacement({ caret: continued.caret });
    return true;
  };
}
