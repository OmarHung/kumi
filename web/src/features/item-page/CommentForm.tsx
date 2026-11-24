import { useIsMutating, useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type FocusEvent, type RefObject, type SubmitEvent } from 'react';
import { addComment } from '../../api/activity';
import { isApiError } from '../../api/http';
import { refreshSubscriptionLater } from '../../api/notificationCache';
import type { Activity } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { draftKey } from '../../lib/draftStorage';
import { prefersReducedMotion } from '../../lib/prefersReducedMotion';
import { useDraft } from '../../lib/useDraft';
import { UploadAwareSubmit } from '../attachments/EditorUploadControls';
import { cellErrorMessage } from '../item-table/cells/EditableCell';
import { appendActivity } from './activityCache';
import { announceCommentPosted, commentMutationKey, onCommentPosted } from './commentPosting';
import type { RichTextEditorHandle } from '../rich-text/types';
import { CommentEditorField, SUBMIT_SHORTCUT, useCommentEditorParts, type CommentEditorParts } from './CommentEditorField';
import { RestoredDraftNotice } from './RestoredDraftNotice';
import { revealInScrollParent } from './scrollToTarget';
import styles from './ActivitySection.module.css';

const COMMENT_ROWS = 3;
const COLLAPSED_ROWS = 1;

/** 只有空白的留言送不出去，也不必留著。 */
const NOT_BLANK = { isWorthKeeping: (value: string) => value.trim() !== '' };

function useAddComment(itemKey: string, onPosted: (activity: Activity) => void, replyTo?: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: commentMutationKey(itemKey, replyTo),
    mutationFn: (body: string) => addComment(itemKey, replyTo === undefined ? { body } : { body, replyTo }),
    // 先讓時間軸知道這則是自己送出的，它出現時才報讀「已送出留言」，不是整則留言。
    // 清掉留言框與草稿交給 announceCommentPosted：送出中換了頁又回來，畫面上的是另一個留言框。
    onSuccess: (activity, body) => {
      onPosted(activity);
      announceCommentPosted(itemKey, body, replyTo);
      // 留言的人會被後端自動關注（約 2 秒後）：之後再問一次關注的狀態。
      refreshSubscriptionLater(queryClient, itemKey);
      return appendActivity(queryClient, itemKey, activity);
    },
  });
}

/** 這個項目的留言送出成功、而且框裡還是送出的那一段（不管是不是這個元件送的）：清掉，「已還原」也收起來。 */
function useClearWhenPosted(itemKey: string, draft: string, clear: () => void, replyTo?: string) {
  useEffect(
    () =>
      onCommentPosted(
        itemKey,
        (body) => {
          if (draft.trim() === body) clear();
        },
        replyTo,
      ),
    [itemKey, draft, clear, replyTo],
  );
}

/**
 * 焦點在留言框裡（編輯器或「送出留言」）嗎？焦點在框裡的元素之間移動不算離開，才不會一按 Tab 就收起來、按鈕跟著不見。
 */
function useFocusWithin() {
  const [hasFocus, setHasFocus] = useState(false);
  return {
    hasFocus,
    onFocus: () => {
      setHasFocus(true);
    },
    onBlur: (event: FocusEvent<HTMLFormElement>) => {
      const next = event.relatedTarget;
      if (!(next instanceof Node && event.currentTarget.contains(next))) setHasFocus(false);
    },
  };
}

/**
 * 從一行展開的那一下（點了、Tab 進來），把整個框捲進看得到的地方：留言框在面板最下面時，展開後的編輯器與「送出留言」
 * 會被面板的底切掉（2026-11-24 使用者回報）。只在展開的那一次捲，打字、送出時不動；掛上時就是展開的（回覆框、還原的草稿）不捲。
 * 等瀏覽器排好展開後的高度（下一個畫格）再量。
 */
function useRevealOnExpand(isExpanded: boolean): RefObject<HTMLFormElement | null> {
  const formRef = useRef<HTMLFormElement>(null);
  const wasExpanded = useRef(isExpanded);
  useEffect(() => {
    const form = formRef.current;
    const isOpening = isExpanded && !wasExpanded.current;
    wasExpanded.current = isExpanded;
    if (!isOpening || form === null || !form.contains(document.activeElement)) return undefined;
    const frame = requestAnimationFrame(() => {
      revealInScrollParent(form, prefersReducedMotion() ? 'auto' : 'smooth');
    });
    return () => {
      cancelAnimationFrame(frame);
    };
  }, [isExpanded]);
  return formRef;
}

/** 回覆框：回覆的是哪一則最上層留言，以及怎麼收起來。 */
export interface ReplyTarget {
  readonly commentId: string;
  /** 那一則的作者：框的名稱是「回覆 作者 的留言」。 */
  readonly authorName: string;
  /** 取消、或框是空的時按 Esc：呼叫端把框收起來。草稿已經清掉。 */
  readonly onClose: () => void;
  /** 使用者按了「回覆」才打開的，焦點進編輯器；換頁回來還原的不搶焦點。 */
  readonly autoFocus: boolean;
}

interface CommentFormProps {
  readonly itemKey: string;
  /** 送出成功時，帶著這則留言的活動。 */
  readonly onPosted: (activity: Activity) => void;
  /** 有給就是某一則留言底下的回覆框（常開、有「取消」、草稿與送出狀態各串分開）。 */
  readonly reply?: ReplyTarget;
}

/** 回覆的那一則找不到（被別的程序移除或項目不對）時，說清楚是哪裡的問題，不只是「找不到」。 */
function postErrorMessage(error: unknown): string {
  if (isApiError(error) && error.code === 'comment.not_found') {
    return '找不到你要回覆的那則留言，它可能已經不在這個項目裡。';
  }
  return cellErrorMessage(error, ['body']);
}

/**
 * 草稿與送出：空白不能送、送出中不能再送；成功後清空，失敗時留著內容，再打字就收掉錯誤。
 * 打的字邊打邊存進這個分頁（lib/useDraft.ts），回到這個項目時還原；送出成功、捨棄時清掉。
 * 成功的回呼放在 useMutation 上：送出中就換了頁，留言送出去之後草稿照樣清掉。
 */
function useCommentDraft({ itemKey, onPosted, reply }: CommentFormProps) {
  const replyTo = reply?.commentId;
  const stored = useDraft(draftKey('comment', itemKey, replyTo), NOT_BLANK);
  const [draft, setDraft] = useState(stored.restored ?? '');
  const mutation = useAddComment(itemKey, onPosted, replyTo);
  // 送出中換了頁又回來：送出的是前一個留言框，這個框還原了同一段字。還在送就當成送出中，不能再送一次。
  const isPending = useIsMutating({ mutationKey: commentMutationKey(itemKey, replyTo) }) > 0 || mutation.isPending;
  useClearWhenPosted(
    itemKey,
    draft,
    () => {
      setDraft('');
      stored.clear();
    },
    replyTo,
  );
  const body = draft.trim();
  const canSend = body !== '' && !isPending;
  return {
    draft,
    canSend,
    isRestored: stored.isRestored,
    isPending,
    error: mutation.isError ? postErrorMessage(mutation.error) : null,
    change: (next: string) => {
      setDraft(next);
      stored.save(next);
      if (mutation.isError) mutation.reset();
    },
    discard: () => {
      setDraft('');
      stored.clear();
    },
    send: () => {
      if (canSend) mutation.mutate(body);
    },
  };
}

/** 最上層的留言框與回覆框用的字：回覆框的名稱帶著回覆的對象（「回覆 作者 的留言」）。 */
function wordingOf(reply: ReplyTarget | undefined) {
  if (reply === undefined) {
    return { label: '留言', formLabel: '新增留言', placeholder: '寫下留言…', notice: '沒送出的留言', submit: '送出留言' };
  }
  const name = `回覆 ${reply.authorName} 的留言`;
  return { label: name, formLabel: name, placeholder: '寫下回覆…', notice: '沒送出的回覆', submit: '送出回覆' };
}

/** 回覆框的「取消」：收起來，打到一半的字也不要了。 */
function CancelReply({ isDisabled, onCancel }: { readonly isDisabled: boolean; readonly onCancel: () => void }) {
  return (
    <Button size="sm" variant="ghost" disabled={isDisabled} onClick={onCancel}>
      取消
    </Button>
  );
}

interface ComposerActionsOptions {
  readonly comment: ReturnType<typeof useCommentDraft>;
  readonly uploads: CommentEditorParts['uploads'];
  readonly editorRef: RefObject<RichTextEditorHandle | null>;
  readonly reply: ReplyTarget | undefined;
}

/** 留言框的送出、捨棄與（回覆框的）收起來。 */
function useComposerActions({ comment, uploads, editorRef, reply }: ComposerActionsOptions) {
  // 還有檔案在上傳時不送：框裡還有「上傳中」的記號，送出去會留在留言裡。
  const send = () => {
    if (uploads.isUploading) uploads.announceUploading();
    else comment.send();
  };
  // 回覆框的 Esc：空的才收起來（有字時什麼都不做，免得一按就丟掉打的字）；取消則連草稿一起不要。
  const closeReply = () => {
    comment.discard();
    reply?.onClose();
  };
  return {
    send,
    closeReply,
    words: wordingOf(reply),
    // 從「送出留言」送出（點一下或在按鈕上按 Enter）時，先把焦點放回編輯器再送：送出中按鈕會停用，
    // 有焦點的按鈕一停用，瀏覽器就把焦點丟回頁面本體。編輯器送出中是唯讀，焦點留得住。
    handleSubmit: (event: SubmitEvent<HTMLFormElement>) => {
      event.preventDefault();
      editorRef.current?.focus();
      send();
    },
    // 「捨棄」跟著說明一起不見：焦點先放到編輯器（還在框裡，留言框不會先收起來）。
    discard: () => {
      editorRef.current?.focus();
      comment.discard();
    },
    escapeReply: () => {
      if (comment.draft.trim() === '') closeReply();
    },
  };
}

interface ComposerActionsProps {
  readonly isPending: boolean;
  readonly canSend: boolean;
  readonly isUploading: boolean;
  readonly submitLabel: string;
  /** 回覆框才有「取消」。 */
  readonly onCancelReply: (() => void) | undefined;
}

/** 編輯器下面的動作列：（回覆框的「取消」）、送出，都靠右。「插入圖片或檔案」在編輯器上方的工具列裡。 */
function ComposerActions({ isPending, canSend, isUploading, submitLabel, onCancelReply }: ComposerActionsProps) {
  return (
    <div className={styles.composerActions}>
      {onCancelReply !== undefined && <CancelReply isDisabled={isPending} onCancel={onCancelReply} />}
      <UploadAwareSubmit isUploading={isUploading} disabled={!canSend}>
        {isPending ? '送出中…' : submitLabel}
      </UploadAwareSubmit>
    </div>
  );
}

/**
 * 留言框：所見即所得（存的是 Markdown）。平常是一行，取得焦點才展開（同一個編輯器只是變高，焦點不會掉）；打了字、送出中、
 * 失敗時一直展開，點「送出留言」時不會因為編輯器失去焦點而先收起來。送出中編輯器唯讀（仍然停得到）；
 * 成功後清空，焦點留在編輯器裡可以接著寫（焦點在框裡，所以照樣展開）；失敗時留著內容，訊息顯示在框下面。
 * 還原了草稿時，框上面說一聲並給「捨棄」：捨棄後清空、焦點回到編輯器（按鈕跟著不見）。
 * 回覆框是使用者特地打開的：一直展開（isExpanded），還有「取消」。
 */
function CommentComposer(props: CommentFormProps) {
  const comment = useCommentDraft(props);
  const focus = useFocusWithin();
  const { candidates, editorRef, uploads } = useCommentEditorParts({ itemKey: props.itemKey, value: comment.draft, onChange: comment.change, isBusy: comment.isPending });
  const { reply } = props;
  const isExpanded = reply !== undefined || focus.hasFocus || comment.draft !== '' || comment.isPending || comment.error !== null;
  const { handleSubmit, send, discard, closeReply, escapeReply, words } = useComposerActions({ comment, uploads, editorRef, reply });
  const formRef = useRevealOnExpand(isExpanded);
  return (
    <form
      ref={formRef}
      className={styles.composer}
      aria-label={words.formLabel}
      aria-busy={comment.isPending}
      data-expanded={isExpanded}
      onSubmit={handleSubmit}
      onFocus={focus.onFocus}
      onBlur={focus.onBlur}
    >
      {comment.isRestored && <RestoredDraftNotice target={words.notice} onDiscard={discard} />}
      <CommentEditorField
        editorRef={editorRef}
        candidates={candidates}
        uploads={uploads}
        label={words.label}
        isLabelHidden
        value={comment.draft}
        onChange={comment.change}
        rows={isExpanded ? COMMENT_ROWS : COLLAPSED_ROWS}
        isCollapsed={!isExpanded}
        placeholder={words.placeholder}
        autoFocus={reply?.autoFocus}
        onCancel={reply === undefined ? undefined : escapeReply}
        hint={`${SUBMIT_SHORTCUT} 送出`}
        onSubmit={send}
        isBusy={comment.isPending}
        error={comment.error}
      />
      <ComposerActions isPending={comment.isPending} canSend={comment.canSend} isUploading={uploads.isUploading} submitLabel={words.submit} onCancelReply={reply === undefined ? undefined : closeReply} />
    </form>
  );
}

/** 換了項目就重新掛上：草稿、送出的狀態都跟著項目，不會把上一個項目打的字帶過去。 */
export function CommentForm(props: CommentFormProps) {
  return <CommentComposer key={`${props.itemKey}:${props.reply?.commentId ?? ''}`} {...props} />;
}
