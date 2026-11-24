import { useEffect, useRef, useState, type RefObject } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { isApiError } from '../../../api/http';
import { knowledgeAttachmentContentUrl } from '../../../api/knowledge';
import type { KnowledgePage } from '../../../api/knowledgeTypes';
import { Button } from '../../../components/Button/Button';
import { Icon } from '../../../components/Icon/Icon';
import { Markdown } from '../../../components/Markdown/Markdown';
import { ErrorState, LoadingState, RefetchErrorNotice } from '../../../components/StatusViews/StatusViews';
import { useUnsavedChangesGuard } from '../../../lib/useUnsavedChangesGuard';
import { knowledgeSpaceUrl } from '../knowledgeLinks';
import { RestrictionDialog } from '../restriction/RestrictionDialog';
import { useKnowledgePage } from '../space/spaceQueries';
import { PageEditForm } from './PageEditForm';
import { PageMeta } from './PageMeta';
import { usePageEditor } from './usePageEditor';
import styles from './KnowledgePage.module.css';

/** 頁面內容沒有成員的提及（第一版的編輯器不開 @ 提及），所以「@名稱」一律照原文。 */
const NO_MENTIONS: ReadonlyMap<string, string> = new Map();
const HTTP_NOT_FOUND = 404;
const PAGE_NOT_FOUND = 'knowledge.page_not_found';

/** 伺服器說這一頁不存在（被刪了、搬走了）：和空間不存在、網址不合格是不同的錯，只認頁面的。 */
function isPageGone(error: unknown): boolean {
  return isApiError(error) && error.status === HTTP_NOT_FOUND && error.code === PAGE_NOT_FOUND;
}

function wantsEdit(state: unknown): boolean {
  return typeof state === 'object' && state !== null && 'edit' in state && state.edit === true;
}

interface PageContentProps {
  readonly page: KnowledgePage;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/**
 * 剛新增的頁面帶著「直接進編輯」的記號（瀏覽紀錄的 state）：掛上時讀一次，之後把記號從這一筆紀錄拿掉。
 * 不拿掉的話，之後按上一頁、下一頁回到這一頁（或重新整理）又會自己進編輯。
 */
function useConsumeEditRequest(): boolean {
  const location = useLocation();
  const navigate = useNavigate();
  const isRequested = wantsEdit(location.state);
  const { pathname, search } = location;
  useEffect(() => {
    if (isRequested) void navigate(`${pathname}${search}`, { replace: true, state: null });
  }, [isRequested, pathname, search, navigate]);
  return isRequested;
}

interface PageHeadProps {
  readonly title: string;
  readonly isEditing: boolean;
  readonly canEdit: boolean;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
  readonly entryRef: RefObject<HTMLButtonElement | null>;
  readonly onStart: () => void;
}

/**
 * 編輯時標題列整個不畫（只留給輔助科技的標題）：標題的輸入框要放在原本標題的位置，
 * 留著空的一列會把輸入框往下推（編輯不推動版面，docs/design/08-interaction.md 8.3）。
 */
function PageHead({ title, isEditing, canEdit, headingRef, entryRef, onStart }: PageHeadProps) {
  if (isEditing) {
    return (
      <h1 ref={headingRef} tabIndex={-1} className="visually-hidden" data-content-start>
        {`編輯「${title}」`}
      </h1>
    );
  }
  return (
    <div className={styles.head}>
      <h1 ref={headingRef} tabIndex={-1} className={styles.title} data-content-start>
        {title}
      </h1>
      {canEdit && (
        <Button ref={entryRef} size="sm" onClick={onStart}>
          <Icon name="edit" />
          編輯
        </Button>
      )}
    </div>
  );
}

function PageContent({ page, headingRef }: PageContentProps) {
  const entryRef = useRef<HTMLButtonElement>(null);
  const [isRestrictionOpen, setIsRestrictionOpen] = useState(false);
  const canEdit = page.myPermissions.canEdit;
  const startsEditing = useConsumeEditRequest();
  // 剛新增的頁面（樹上的輸入框建立之後）直接進編輯；沒有 canEdit 的人（權限剛被拿掉）不還原：存著的草稿留在分頁裡。
  const editor = usePageEditor(page, entryRef, startsEditing && canEdit);
  const isEditing = canEdit && editor.draft !== null;
  const { dialog } = useUnsavedChangesGuard(isEditing && editor.hasChanges, '離開這一頁的話，標題與內容的修改會不見。');

  return (
    <article className={styles.page} aria-label={page.title}>
      <PageHead
        title={page.title}
        isEditing={isEditing}
        canEdit={canEdit}
        headingRef={headingRef}
        entryRef={entryRef}
        onStart={editor.start}
      />
      {isEditing && editor.draft !== null ? (
        <PageEditForm editor={editor} draft={editor.draft} page={page} />
      ) : (
        <>
          <PageMeta
            page={page}
            onOpenRestriction={() => {
              setIsRestrictionOpen(true);
            }}
          />
          <PageBody page={page} canEdit={canEdit} onStart={editor.start} />
        </>
      )}
      {dialog}
      {isRestrictionOpen && (
        <RestrictionDialog
          spaceKey={page.space.key}
          pageNumber={page.number}
          pageTitle={page.title}
          projectKey={page.space.project?.key ?? null}
          canEdit={canEdit}
          onClose={() => {
            setIsRestrictionOpen(false);
          }}
        />
      )}
    </article>
  );
}

function PageBody({ page, canEdit, onStart }: { readonly page: KnowledgePage; readonly canEdit: boolean; readonly onStart: () => void }) {
  if (page.body !== '') return <Markdown text={page.body} mentionNames={NO_MENTIONS} attachmentUrl={knowledgeAttachmentContentUrl} />;
  if (!canEdit) return <p className={styles.empty}>這一頁還沒有內容。</p>;
  return (
    <button type="button" className={styles.add} onClick={onStart}>
      <span className={styles.addLabel}>
        <Icon name="add" />
        開始寫這一頁
      </span>
      <span className={styles.addHint}>寫下想讓同事找得到的內容。</span>
    </button>
  );
}

interface KnowledgePageViewProps {
  readonly spaceKey: string;
  readonly pageNumber: number;
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
}

/** 找不到（網址上的編號不合格、頁面不存在、或看不到）：左邊的樹照舊，這裡說明並回到空間首頁。 */
export function PageNotFound({ spaceKey, headingRef }: { readonly spaceKey: string; readonly headingRef: RefObject<HTMLHeadingElement | null> }) {
  return (
    <section className={styles.page} data-content-start>
      <h1 ref={headingRef} tabIndex={-1} className={styles.title}>
        找不到這一頁
      </h1>
      <p className={styles.empty}>這一頁可能已經被刪除、搬走，或是你沒有權限看它。</p>
      <p>
        <Link to={knowledgeSpaceUrl(spaceKey)}>回到空間首頁</Link>
      </p>
    </section>
  );
}

/** 一頁：載入、失敗、找不到、看、編輯。換了頁就重新掛上（草稿與編輯中的狀態跟著頁面）。 */
export function KnowledgePageView({ spaceKey, pageNumber, headingRef }: KnowledgePageViewProps) {
  const query = useKnowledgePage(spaceKey, pageNumber);
  if (query.isPending) return <LoadingState label="正在載入頁面…" />;
  // 手上有舊資料、重抓時才說找不到（別人刪了它）：舊內容不能再看、更不能再編輯。
  if (query.isError && isPageGone(query.error)) return <PageNotFound spaceKey={spaceKey} headingRef={headingRef} />;
  if (query.isLoadingError) {
    if (isApiError(query.error) && query.error.status === HTTP_NOT_FOUND) return <PageNotFound spaceKey={spaceKey} headingRef={headingRef} />;
    return (
      <ErrorState
        error={query.error}
        title="頁面載入失敗"
        onRetry={() => {
          void query.refetch();
        }}
      />
    );
  }
  return (
    <>
      <RefetchErrorNotice query={query} message="無法更新這一頁的最新內容，畫面上可能是舊的。" />
      <PageContent key={pageNumber} page={query.data} headingRef={headingRef} />
    </>
  );
}
