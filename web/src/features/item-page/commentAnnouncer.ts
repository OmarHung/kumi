import { createContext, useContext } from 'react';

/** 給報讀軟體唸一句話（儲存、刪除、復原成功）；實際的 live region 在 ActivitySection。 */
export type CommentAnnounce = (text: string) => void;

const NOOP: CommentAnnounce = () => undefined;

export const CommentAnnouncerContext = createContext<CommentAnnounce>(NOOP);

export function useCommentAnnouncer(): CommentAnnounce {
  return useContext(CommentAnnouncerContext);
}
