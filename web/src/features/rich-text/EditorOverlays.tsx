import type { DragEvent, RefObject } from 'react';
import type { Member } from '../../api/types';
import { MentionList } from '../item-page/mentions/MentionList';
import type { EditorUploads } from '../attachments/useEditorUploads';

/** 提及的清單要的狀態：所見即所得的 useEditorMention 與 Markdown 原文的 useMentionInput 都符合。 */
export interface MentionOverlayState {
  readonly isOpen: boolean;
  readonly listId: string;
  readonly options: readonly Member[];
  readonly active: number;
  readonly announcement: string;
  readonly point: (index: number) => void;
  readonly choose: (index: number) => void;
  readonly dismiss: () => void;
}

/** 提及的清單（打 @ 時才掛上，浮在外框旁邊）；即時區域一直掛著（先掛空的再填字，報讀軟體才唸得到）。 */
export function MentionOverlay({ mention, anchorRef }: { readonly mention: MentionOverlayState; readonly anchorRef: RefObject<HTMLElement | null> }) {
  return (
    <>
      {mention.isOpen && (
        <MentionList
          listId={mention.listId}
          options={mention.options}
          active={mention.active}
          anchorRef={anchorRef}
          onPoint={mention.point}
          onChoose={mention.choose}
          onAnchorHidden={mention.dismiss}
        />
      )}
      <p className="visually-hidden" aria-live="polite">
        {mention.announcement}
      </p>
    </>
  );
}

/** 拖檔案的外框狀態：滑到外框裡的子元素時不算離開。 */
export function dragHandlersOf(uploads: EditorUploads | undefined, isBusy: boolean) {
  const handlers = uploads?.binding.handlers;
  if (handlers === undefined) return undefined;
  return {
    onDragEnter: (event: DragEvent<HTMLDivElement>) => {
      handlers.onDragEnter(event);
    },
    onDragOver: (event: DragEvent<HTMLDivElement>) => {
      handlers.onDragOver(event);
    },
    onDragLeave: (event: DragEvent<HTMLDivElement>) => {
      if (event.relatedTarget instanceof Node && event.currentTarget.contains(event.relatedTarget)) return;
      handlers.onDragLeave();
    },
    // 落在編輯器裡的由編輯器的 handleDrop 處理過了（preventDefault）；落在工具列、內距這類地方的照一般的放到游標。送出中一律忽略。
    onDrop: (event: DragEvent<HTMLDivElement>) => {
      if (event.defaultPrevented) return;
      if (isBusy) event.preventDefault();
      else handlers.onDrop(event);
    },
  };
}
