import { useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import { useProjects } from '../../api/queries';
import type { Project } from '../../api/types';
import { isOrganizationAdmin, useCurrentUser } from '../../auth/CurrentUser';
import { Button } from '../../components/Button/Button';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ArrivalNotice, EmptyState, ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { useArrivalNotice } from '../../lib/useArrivalNotice';
import { SearchField } from '../item-table/FilterFields';
import { NewProjectDialog } from './NewProjectDialog';
import { ProjectGroup } from './ProjectGroup';
import { ProjectSearchResults } from './ProjectSearchResults';
import { normalizeProjectSearchText, PROJECT_SEARCH_MAX_LENGTH } from './projectSearchText';
import styles from './ProjectListPage.module.css';

/** 第一次載入的骨架：大約是一個組織同時進行的專案數。 */
const SKELETON_ROWS = 4;

const SEARCH_PARAM = 'q';

/** 已封存的專案放在清單下面，自己一個標題；封存的專案只能看，但還找得到、可以取消封存。 */
function ArchivedProjects({ projects, isFirstGroup }: { readonly projects: readonly Project[]; readonly isFirstGroup: boolean }) {
  return (
    <section className={styles.archived} aria-labelledby="archived-projects-heading">
      <h2 id="archived-projects-heading" className={styles.archivedHeading}>
        已封存的專案
      </h2>
      <ProjectGroup projects={projects} isFirstGroup={isFirstGroup} label="已封存的專案" hasArchivedDate />
    </section>
  );
}

/** 沒有輸入搜尋時的清單：沒封存的一組，已封存的在下面自己一組。 */
function ProjectGroups({ projects }: { readonly projects: readonly Project[] }) {
  const active = projects.filter((project) => project.archivedAt === null);
  const archived = projects.filter((project) => project.archivedAt !== null);
  return (
    <>
      {active.length > 0 && <ProjectGroup projects={active} isFirstGroup />}
      {archived.length > 0 && <ArchivedProjects projects={archived} isFirstGroup={active.length === 0} />}
    </>
  );
}

/** 網址的 ?q=：搜尋的字記在網址上（重新整理、分享連結都還在）；改字不留瀏覽紀錄，清空就拿掉。 */
function useSearchText(): readonly [string, (next: string) => void] {
  const [params, setParams] = useSearchParams();
  // 網址可以手改：太長或有控制字元的 q 整理過才用，不送一個一定 422 的請求。
  const text = normalizeProjectSearchText(params.get(SEARCH_PARAM) ?? '');
  const setText = (next: string) => {
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        const normalized = normalizeProjectSearchText(next);
        if (normalized === '') updated.delete(SEARCH_PARAM);
        else updated.set(SEARCH_PARAM, normalized);
        return updated;
      },
      { replace: true },
    );
  };
  return [text, setText];
}

function ProjectListContent({ canCreate }: { readonly canCreate: boolean }) {
  const projects = useProjects();
  const [searchText, setSearchText] = useSearchText();
  if (projects.isPending) {
    return <Skeleton label="正在載入專案…" rows={SKELETON_ROWS} />;
  }
  // 只有第一次就讀不到才換成錯誤；已經有清單時重抓失敗，留著清單、多一行提示。
  if (projects.isLoadingError) {
    return (
      <ErrorState
        error={projects.error}
        onRetry={() => {
          void projects.refetch();
        }}
      />
    );
  }
  const notice = <RefetchErrorNotice query={projects} message="無法更新專案清單，畫面上可能是舊的。" />;
  if (projects.data.length === 0) {
    const hint = canCreate ? '按「新增專案」，從模板開第一個專案。' : '請專案的管理員把你加進專案，加入後會出現在這裡。';
    return (
      <>
        {notice}
        <EmptyState title="還沒有專案">{hint}</EmptyState>
      </>
    );
  }
  return (
    <>
      {notice}
      <div className={styles.search}>
        <SearchField
          isToolbar
          label="搜尋專案"
          placeholder="搜尋代號、名稱或說明，例如主機名稱"
          maxLength={PROJECT_SEARCH_MAX_LENGTH}
          value={searchText}
          onSearch={setSearchText}
        />
      </div>
      {searchText === '' ? <ProjectGroups projects={projects.data} /> : <ProjectSearchResults text={searchText} />}
    </>
  );
}

/** 把自己移出專案之後回到這裡時（navigate 的 state），焦點到頁面的標題、提示填進一直掛著的 status。 */
export function ProjectListPage() {
  const user = useCurrentUser();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const arrival = useArrivalNotice(headingRef);
  const canCreate = isOrganizationAdmin(user);
  const [isCreating, setIsCreating] = useState(false);

  // 沒有權限的人沒有入口（頁列就沒有動作區）。
  const createButton = canCreate ? (
    <Button
      variant="primary"
      onClick={() => {
        setIsCreating(true);
      }}
    >
      新增專案
    </Button>
  ) : undefined;

  return (
    <PageLayout variant="list" bar={<PageBar title="專案" actions={createButton} headingRef={headingRef} />}>
      <ArrivalNotice message={arrival} />
      <ProjectListContent canCreate={canCreate} />
      {isCreating && (
        <NewProjectDialog
          onClose={() => {
            setIsCreating(false);
          }}
        />
      )}
    </PageLayout>
  );
}
