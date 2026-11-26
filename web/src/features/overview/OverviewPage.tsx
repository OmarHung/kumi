import { useRef } from 'react';
import { Link, useSearchParams } from 'react-router';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { buttonClassName } from '../../components/Button/Button';
import { EmptyState, ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { useIsCompact } from '../../lib/useMediaQuery';
import { useNow } from '../../lib/useNow';
import { OverviewCards } from './OverviewCards';
import { OverviewNotes } from './OverviewNotes';
import { OverviewTable } from './OverviewTable';
import { OverviewToolbar } from './OverviewToolbar';
import { overviewScopeOf, overviewSortOf, sortOverviews, withOverviewView } from './overviewView';
import { useProjectOverviews } from './useProjectOverviews';
import styles from './Overview.module.css';

/** 第一次載入的骨架：大約是一個 PM 同時管的專案數。 */
const SKELETON_ROWS = 6;

/** 沒有專案可列。只看管理的專案時，多半是還沒被指派成專案管理員：提示切到全部看得到的。 */
function EmptyOverview({ isManagedScope }: { readonly isManagedScope: boolean }) {
  const [params] = useSearchParams();
  if (!isManagedScope) {
    return <EmptyState title="還沒有看得到的專案">請專案的管理員把你加進專案，加入後會出現在這裡。</EmptyState>;
  }
  const visible = withOverviewView(params, { scope: 'visible', sort: overviewSortOf(params) }).toString();
  return (
    <EmptyState
      title="你還沒有管理的專案"
      action={
        <Link to={{ search: `?${visible}` }} className={buttonClassName('secondary', 'md')}>
          看全部看得到的專案
        </Link>
      }
    >
      專案總覽預設只列你管理（有專案管理權限）的專案。
    </EmptyState>
  );
}

function OverviewContent() {
  const [params] = useSearchParams();
  const isCompact = useIsCompact();
  const scope = overviewScopeOf(params);
  const sort = overviewSortOf(params);
  const overviews = useProjectOverviews(scope);
  const now = useNow();
  if (overviews.isPending) return <Skeleton label="正在載入專案總覽…" rows={SKELETON_ROWS} />;
  // 只有第一次就讀不到才換成錯誤；已經有資料時重抓失敗，留著數字、多一行提示。
  if (overviews.isLoadingError) {
    return (
      <ErrorState
        error={overviews.error}
        title="專案總覽載入失敗"
        onRetry={() => {
          void overviews.refetch();
        }}
      />
    );
  }
  const notice = <RefetchErrorNotice query={overviews} message="無法更新專案總覽，畫面上可能是舊的。" />;
  if (overviews.data.length === 0) {
    return (
      <>
        {notice}
        <EmptyOverview isManagedScope={scope === 'managed'} />
      </>
    );
  }
  const projects = sortOverviews(overviews.data, sort);
  const List = isCompact ? OverviewCards : OverviewTable;
  return (
    <>
      {notice}
      <div className={styles.results} aria-busy={overviews.isFetching || overviews.isPlaceholderData}>
        <List projects={projects} now={now} />
      </div>
      <OverviewNotes />
    </>
  );
}

/** 專案總覽（/overview）：一次看多個專案的進度，點數字帶著篩選到專案的表格（internal/HANDOFF-project-overview.md 第 5 節）。 */
export function OverviewPage() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  return (
    <PageLayout variant="list" bar={<PageBar title="專案總覽" headingRef={headingRef} />} toolbar={<OverviewToolbar />}>
      <OverviewContent />
    </PageLayout>
  );
}
