import { useEffect, useRef, type ReactNode } from 'react';
import { useLocation, useParams } from 'react-router';
import { PageBar, type Crumb } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { SubNav, SubNavSelect } from '../../components/SubNav/SubNav';
import { scrollToPageTop } from '../../lib/pageTop';
import { useIsBelowWide } from '../../lib/useMediaQuery';
import { NotFoundPage } from '../errors/NotFoundPage';
import { QuickStart } from './chapters/QuickStart';
import { HELP_LABEL, HELP_PATH, findHelpChapter, helpNavGroups, type HelpChapterKey } from './helpChapters';
import styles from './Help.module.css';

const CRUMBS: readonly Crumb[] = [{ label: HELP_LABEL }];

const CHAPTERS: Readonly<Record<HelpChapterKey, () => ReactNode>> = {
  'quick-start': () => <QuickStart />,
};

/** 換章時捲回最上面（同一個路由、頁面不重新掛上，瀏覽器不會自己捲）；網址帶著錨點時讓瀏覽器捲到那一節。 */
function useScrollTopOnChapterChange(pathname: string, hash: string) {
  const previous = useRef(pathname);
  useEffect(() => {
    if (previous.current === pathname) return;
    previous.current = pathname;
    if (hash === '') scrollToPageTop();
  }, [pathname, hash]);
}

/**
 * 使用說明（docs/DESIGN.md 6.2「使用說明」）：給第一次用的人看的手冊，一章一頁。閱讀頁，版面和設定中心一樣：
 * 寬螢幕左邊是章節的次導覽，窄螢幕換成工具列上的下拉。內容是程式裡寫好的元件（不是 Markdown）：
 * 裡面的「我的工作」「設定 → 通知」直接連到那一頁，標題也是頁面真正的結構。
 */
export function HelpPage() {
  const { chapter: segment } = useParams();
  const { pathname, hash } = useLocation();
  useScrollTopOnChapterChange(pathname, hash);
  const isBelowWide = useIsBelowWide();
  const chapter = findHelpChapter(segment);

  if (chapter === undefined) {
    return <NotFoundPage title="找不到這一章" message="網址可能打錯了，或是這一章已經改名。" backTo={HELP_PATH} backLabel="回到使用說明" />;
  }

  const groups = helpNavGroups();
  return (
    <PageLayout
      variant="reading"
      bar={<PageBar title={chapter.label} documentTitle={`${chapter.label} · ${HELP_LABEL}`} crumbs={CRUMBS} />}
      toolbar={
        isBelowWide ? (
          <SubNavSelect label={HELP_LABEL} selectLabel="使用說明的章節" groups={groups} currentKey={chapter.key} />
        ) : undefined
      }
    >
      <div className={styles.help} data-has-nav={!isBelowWide}>
        {!isBelowWide && <SubNav label={HELP_LABEL} groups={groups} currentKey={chapter.key} />}
        <article className={styles.article} aria-label={chapter.label}>
          {CHAPTERS[chapter.key]()}
        </article>
      </div>
    </PageLayout>
  );
}
