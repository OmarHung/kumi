import type { Editor } from '@tiptap/core';
import { useCallback, useEffect, useId, useRef, useState, useSyncExternalStore } from 'react';
import type { Member } from '../../api/types';
import { mentionEmptyId, mentionOptionId } from '../item-page/mentions/MentionList';
import { MentionStore } from './extensions/MentionStore';
import { createMentionSuggestion, mentionPluginKey } from './extensions/mentionSuggestion';

/** 編輯器元素上要加的屬性（清單打開時才有）：編輯器一直是 textbox，開關與符合的人數靠一直掛著的 aria-live 說。 */
export interface MentionAria {
  readonly 'aria-controls'?: string;
  readonly 'aria-autocomplete'?: 'list';
  readonly 'aria-activedescendant'?: string;
}

function announcementFor(count: number | null): string {
  if (count === null) return '';
  return count === 0 ? '沒有符合的成員' : `${String(count)} 位成員可以提及，用上下鍵選擇，Enter 確定`;
}

interface MentionOptions {
  readonly members: readonly Member[] | undefined;
  readonly isBusy: boolean;
}

/**
 * 編輯器裡打 @ 的提及（Tiptap 的 Suggestion 接到 features/item-page/mentions 的 MentionList 與 filterMembers，
 * docs/DESIGN.md 8.3「提及」）。suggestion 要交給 Mention 擴充（建立編輯器之前），setEditor 在編輯器建好之後呼叫。
 */
export function useEditorMention({ members, isBusy }: MentionOptions) {
  const listId = useId();
  const [store] = useState(() => new MentionStore());
  const [suggestion] = useState(() => createMentionSuggestion(store, store.dismissed));
  const editorRef = useRef<Editor | null>(null);
  const { session, active } = useSyncExternalStore(store.subscribe, store.getSnapshot);
  useEffect(() => {
    store.configure(members, isBusy);
  }, [store, members, isBusy]);

  /** 關上清單（編輯器被捲出看得到的範圍）；同一個 @ 不再打開。 */
  const dismiss = useCallback(() => {
    const editor = editorRef.current;
    if (editor === null || !store.isOpen()) return;
    editor.view.dispatch(editor.state.tr.setMeta(mentionPluginKey, { exit: true }));
  }, [store]);

  const setEditor = useCallback((editor: Editor | null) => {
    editorRef.current = editor;
  }, []);

  const options = session?.items ?? [];
  const activeId = options.length > 0 ? mentionOptionId(listId, active) : mentionEmptyId(listId);
  const aria: MentionAria = session === null ? {} : { 'aria-controls': listId, 'aria-autocomplete': 'list', 'aria-activedescendant': activeId };
  return {
    suggestion,
    isOpen: session !== null,
    isOpenNow: () => store.isOpen(),
    listId,
    options,
    active,
    aria,
    announcement: announcementFor(session === null ? null : options.length),
    point: (index: number) => {
      store.point(index);
    },
    choose: (index: number) => {
      store.choose(index);
    },
    dismiss,
    setEditor,
  };
}
