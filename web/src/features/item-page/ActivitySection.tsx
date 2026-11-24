import { useEffect, useId, useMemo, useRef, useState, type RefObject } from 'react';
import { errorMessage } from '../../api/http';
import { useItemActivity } from '../../api/queries';
import type { Activity, ActivityPage, ItemDetail } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import { activityAnnouncement, attachmentGroupAnnouncement, groupAttachmentRuns, typesAtEntries, type ActivityContext } from '../../domain/activity';
import { isFocusLost } from '../../lib/updateThenFocus';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { usePlanning } from '../item-table/usePlanning';
import { setActivityView, useActivityView, type ActivityView } from './activityView';
import { CommentAnnouncerContext } from './commentAnnouncer';
import { CommentForm } from './CommentForm';
import { SectionHeading } from './ItemPanelContext';
import { ThreadEntry } from './ThreadEntry';
import { buildThreads } from './threads';
import { useCommentTarget } from './useCommentTarget';
import styles from './ActivitySection.module.css';

const NO_ACTIVITY: readonly Activity[] = [];

type ActivityResult = ReturnType<typeof useItemActivity>;

/** 每一筆活動的說法要用的內容：狀態的名稱看當時的類型（typesAtEntries）。 */
type ContextOf = (activity: Activity) => ActivityContext;

/** 自己送出的留言只說「已送出留言」；其他新的活動唸簡短的摘要，太多筆時只說幾筆。 */
function announcementFor(added: readonly Activity[], postedId: string | null, contextOf: ContextOf): string {
  const others = added.filter((entry) => entry.id !== postedId);
  const posted = added.find((entry) => entry.id === postedId);
  const parts = posted === undefined ? [] : [posted.replyToId === null ? '已送出留言' : '已送出回覆'];
  // 連續附加的檔案畫成一行，報讀也是一句（和時間軸上看到的筆數一致）。
  const groups = groupAttachmentRuns(others);
  const [only] = groups;
  const [first] = only ?? [];
  if (groups.length === 1 && only !== undefined && first !== undefined) {
    parts.push(only.length > 1 ? attachmentGroupAnnouncement(only) : activityAnnouncement(first, contextOf(first)));
  } else if (groups.length > 1) {
    parts.push(`多了 ${String(groups.length)} 筆活動`);
  }
  return parts.join('。');
}

/**
 * 時間軸多了新的一筆（留言、修改後重抓、載入更多）時，給螢幕報讀器的一句話。
 * 第一次載入不唸：那是頁面本來的內容。換看哪一份（留言／全部）也當作第一次載入：那是使用者自己按的，不是新活動。
 * postedId 是自己剛送出的留言。
 */
function useNewEntryAnnouncement(
  view: ActivityView,
  entries: readonly Activity[],
  isLoaded: boolean,
  contextOf: ContextOf,
  postedId: string | null,
): string {
  const newestId = entries.at(-1)?.id ?? null;
  // undefined 表示這一份還沒載入過；之後記住上一次看到的最新一筆。記的時候連看哪一份一起記：換了就重來。
  const [seen, setSeen] = useState<{ readonly view: ActivityView; readonly newestId: string | null } | undefined>(undefined);
  // 訊息連同它是哪一份的一起記：換了份就先當成沒有，不等新的那份載入完才清掉上一份的訊息。
  const [message, setMessage] = useState<{ readonly view: ActivityView; readonly text: string }>({ view, text: '' });
  const seenNewestId = seen?.view === view ? seen.newestId : undefined;
  if (isLoaded && (seen?.view !== view || newestId !== seenNewestId)) {
    setSeen({ view, newestId });
    if (seenNewestId === undefined) {
      setMessage({ view, text: '' });
    } else {
      const added = entries.slice(entries.findIndex((entry) => entry.id === seenNewestId) + 1);
      setMessage({ view, text: announcementFor(added, postedId, contextOf) });
    }
  }
  return message.view === view ? message.text : '';
}

/**
 * 按了「載入更多」、而且載完的是最後一頁時，按鈕會跟著不見：焦點放到新載入的第一筆（和下層項目同一套，ChildItems.tsx）。
 * 還有下一頁時按鈕留著，焦點本來就在上面；載入時使用者把焦點移到別處，就不搶。
 * 「新的」以按下時已經有的編號判斷：時間軸照時間排，剛送出的留言可能排在新載入的那些後面。
 * 一筆活動不是互動元素，要放焦點的那一筆給 tabIndex=-1（Tab 不停在它上面）。
 */
function useFocusAfterLastPage(view: ActivityView, result: ActivityResult, entries: readonly Activity[]) {
  const listRef = useRef<HTMLOListElement>(null);
  // 連按下時看的是哪一份一起記：換了份，那份的載入不歸這一份，也不能拿它的編號去比另一份的清單。
  const pending = useRef<{ readonly view: ActivityView; readonly ids: ReadonlySet<string> } | null>(null);
  const isFetching = result.isFetchingNextPage;
  const { hasNextPage } = result;
  useEffect(() => {
    const seen = pending.current;
    if (seen === null) return;
    if (seen.view !== view) {
      pending.current = null;
      return;
    }
    if (isFetching) return;
    pending.current = null;
    if (hasNextPage || !isFocusLost()) return;
    focusEntry(listRef, seen.ids);
  }, [view, isFetching, hasNextPage, entries]);
  return {
    listRef,
    loadMore: () => {
      if (isFetching) return;
      pending.current = { view, ids: new Set(entries.map((entry) => entry.id)) };
      void result.fetchNextPage();
    },
  };
}

/** 畫面上由上到下第一筆不在 seen 裡的（回覆在它那一串裡，所以照畫面的順序找）；都看過就第一筆。 */
function focusEntry(listRef: RefObject<HTMLOListElement | null>, seen: ReadonlySet<string>): void {
  const all = Array.from(listRef.current?.querySelectorAll<HTMLElement>('[data-activity-id]') ?? []);
  const entry = all.find((candidate) => !seen.has(candidate.dataset.activityId ?? '')) ?? all[0];
  if (entry === undefined) return;
  entry.tabIndex = -1;
  entry.focus();
}

interface LoadMoreProps {
  readonly result: ActivityResult;
  readonly onLoadMore: () => void;
}

/** 載入中用 aria-disabled 而不是 disabled：有焦點的按鈕一停用，焦點就掉回頁面本體，報讀軟體聽不到「載入中…」。 */
function LoadMore({ result, onLoadMore }: LoadMoreProps) {
  if (!result.hasNextPage && !result.isFetchNextPageError) {
    return null;
  }
  const isFetching = result.isFetchingNextPage;
  return (
    <div className={styles.footer}>
      {result.hasNextPage && (
        <Button size="sm" aria-disabled={isFetching || undefined} onClick={onLoadMore}>
          {isFetching ? '載入中…' : '載入更多'}
        </Button>
      )}
      {result.isFetchNextPageError && (
        <p role="alert" className={styles.loadError}>
          載入更多失敗：{errorMessage(result.error)}
        </p>
      )}
    </div>
  );
}

const VIEW_OPTIONS: readonly { readonly value: ActivityView; readonly label: string }[] = [
  { value: 'comments', label: '留言' },
  { value: 'all', label: '全部' },
];

/** 依看哪一份換的字：清單的名稱、空的、載入中、失敗。 */
const VIEW_TEXT: Readonly<Record<ActivityView, { readonly list: string; readonly empty: string; readonly loading: string; readonly failed: string }>> = {
  comments: { list: '留言', empty: '還沒有留言。', loading: '正在載入留言…', failed: '留言載入失敗' },
  all: { list: '活動與留言', empty: '還沒有任何活動。', loading: '正在載入活動…', failed: '活動載入失敗' },
};

interface TimelineProps {
  readonly view: ActivityView;
  readonly itemKey: string;
  readonly result: ActivityResult;
  readonly entries: readonly Activity[];
  readonly contextOf: ContextOf;
  readonly canComment: boolean;
  readonly onPosted: (activity: Activity) => void;
}

function Timeline({ view, itemKey, result, entries, contextOf, canComment, onPosted }: TimelineProps) {
  const { listRef, loadMore } = useFocusAfterLastPage(view, result, entries);
  const flash = useCommentTarget({ itemKey, entries, result, listRef });
  const target = entries.find((entry) => entry.id === flash?.id);
  // 焦點不搬（面板標題的規範），所以用一句話讓報讀軟體知道捲到了哪裡；和「新活動」的 status 分開，不互相蓋掉。
  const targetNoun = target !== undefined && target.replyToId !== null ? '回覆' : '留言';
  const targetAnnouncement = target === undefined ? '' : `已捲到 ${target.actor.displayName} 的${targetNoun}。`;
  const threads = useMemo(() => buildThreads(entries), [entries]);
  const text = VIEW_TEXT[view];
  if (result.isPending) {
    return <Skeleton label={text.loading} variant="text" />;
  }
  if (result.data === undefined) {
    const retry = () => {
      void result.refetch();
    };
    return <ErrorState error={result.error} title={text.failed} onRetry={retry} />;
  }
  if (entries.length === 0) {
    return <p className={styles.empty}>{text.empty}</p>;
  }
  return (
    <>
      <ol ref={listRef} className={styles.timeline} aria-label={text.list}>
        {threads.map((thread) => (
          <ThreadEntry
            key={thread.activity.id}
            itemKey={itemKey}
            thread={thread}
            contextOf={contextOf}
            now={result.dataUpdatedAt}
            flash={flash}
            canComment={canComment}
            onPosted={onPosted}
          />
        ))}
      </ol>
      <p role="status" className="visually-hidden">
        {targetAnnouncement}
      </p>
      <LoadMore result={result} onLoadMore={loadMore} />
    </>
  );
}

/** 同一筆活動可能出現在兩頁（留言先接在最後，之後載入的頁又帶到它），以編號去重，照時間排。 */
function flatten(data: { readonly pages: readonly ActivityPage[] } | undefined): readonly Activity[] {
  const all = data?.pages.flatMap((page) => page.items) ?? NO_ACTIVITY;
  // 留最後出現的那一個：後面那頁是伺服器排好的位置。
  const unique = all.filter((entry, index) => all.findLastIndex((other) => other.id === entry.id) === index);
  return unique.toSorted((a, b) => Date.parse(a.at) - Date.parse(b.at));
}

/** 活動說法用的清單：還沒載入是 undefined（寫刪節號）、讀不到是 null（中性的說法）。 */
function namesOf<T>(rows: readonly T[], isPending: boolean, isFailed: boolean): readonly T[] | null | undefined {
  if (isFailed) return null;
  return isPending ? undefined : rows;
}

/**
 * 編輯、刪除、復原留言成功的那一句：和「新活動」的摘要分開（那個是由活動清單算出來的，這個是自己做完一件事）。
 * 連著兩次同一句（刪掉兩則留言）文字沒變，live region 不會再唸：每次尾巴交替加一個看不見的字元。
 */
function useCommentNotice() {
  const [notice, setNotice] = useState({ text: '', tick: 0 });
  const announce = (text: string) => {
    setNotice((previous) => ({ text, tick: previous.tick + 1 }));
  };
  return [notice, announce] as const;
}

function CommentNoticeStatus({ notice }: { readonly notice: { readonly text: string; readonly tick: number } }) {
  return (
    <p role="status" className="visually-hidden">
      {notice.text}
      {notice.tick > 0 && notice.tick % 2 === 0 ? '\u200b' : ''}
    </p>
  );
}

/** 活動與留言：由舊到新，往後一頁一頁接；有 comment.create 的人在最下面留言。 */
export function ActivitySection({ item }: { readonly item: ItemDetail }) {
  const { detail } = useProjectTable();
  const headingId = useId();
  const view = useActivityView();
  const result = useItemActivity(item.key, view);
  // 活動只記週期與里程碑的編號：名稱用目前的（改名之後跟著變），清單還沒載入時說法用刪節號、讀不到時用中性的說法（null）。
  const planning = usePlanning(item.projectKey);
  const entries = useMemo(() => flatten(result.data), [result.data]);
  const types = useMemo(() => typesAtEntries(entries, item.type), [entries, item.type]);
  const contextOf: ContextOf = (activity) => ({
    blueprint: detail.blueprint,
    members: detail.members,
    cycles: namesOf(planning.cycles, planning.isCyclesPending, planning.isCyclesFailed),
    milestones: namesOf(planning.milestones, planning.isMilestonesPending, planning.isMilestonesFailed),
    typeKey: types.get(activity.id) ?? item.type,
  });
  const [postedId, setPostedId] = useState<string | null>(null);
  const announcement = useNewEntryAnnouncement(view, entries, result.data !== undefined, contextOf, postedId);
  const canComment = detail.myPermissions.includes('comment.create');
  const onPosted = (activity: Activity) => {
    setPostedId(activity.id);
  };
  const [commentNotice, announceComment] = useCommentNotice();
  return (
    <CommentAnnouncerContext value={announceComment}>
      <section className={styles.section} aria-labelledby={headingId}>
        <div className={styles.headingRow}>
          <SectionHeading id={headingId} className={styles.heading}>
            活動與留言
          </SectionHeading>
          <SegmentedControl label="顯示" options={VIEW_OPTIONS} value={view} onChange={setActivityView} />
        </div>
        <Timeline
          view={view}
          itemKey={item.key}
          result={result}
          entries={entries}
          contextOf={contextOf}
          canComment={canComment}
          onPosted={onPosted}
        />
        <CommentNoticeStatus notice={commentNotice} />
        <p role="status" className="visually-hidden">
          {announcement}
        </p>
        {canComment && <CommentForm itemKey={item.key} onPosted={onPosted} />}
      </section>
    </CommentAnnouncerContext>
  );
}
