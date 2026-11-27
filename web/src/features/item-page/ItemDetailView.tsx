import { useEffect, useRef, type ReactNode } from 'react';
import { useLocation } from 'react-router';
import type { ItemDetail, ProjectDetail } from '../../api/types';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { useArrivalNotice } from '../../lib/useArrivalNotice';
import { useIsCompact } from '../../lib/useMediaQuery';
import { UndoableArrival } from '../bulk-edit/UndoableArrival';
import { ProjectTableProvider } from '../item-table/ProjectTableContext';
import { ArchivedNotice } from '../projects/ArchivedNotice';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { entryReturnTo } from '../work-items/itemLinks';
import { FollowButton } from './FollowButton';
import { ShareItemButton } from './ShareItemButton';
import { ItemActions } from './ItemActions';
import { ItemArticle, useItemScope, useRecordRecentItem } from './ItemContent';
import { itemCrumbs } from './ItemHeader';
import { ItemPanelHost } from './ItemPanelHost';
import { rememberReturnFocus } from './returnFocus';

interface ItemDetailViewProps {
  readonly detail: ProjectDetail;
  readonly item: ItemDetail;
  /** 背景重抓失敗的提示：放在內容最上面固定的位置，出現或消失都不會讓頁面重新掛載。 */
  readonly notices?: ReactNode;
}

/**
 * 麵包屑的專案連結（docs/DESIGN.md 8.1 第 5 點「回得去」）：從這個專案的表格或看板連進來的回到那裡（檢視與條件照舊），
 * 其他的（我的工作、直接打網址）照這個專案上次用的檢視開。
 */
function useProjectCrumbLink(projectKey: string): string {
  const remembered = useProjectPageLink(projectKey);
  return entryReturnTo(projectKey, useLocation().state) ?? remembered;
}

/**
 * 載入好的項目頁（閱讀頁）。頁列是位置與「更多動作」（分頁的標題是「編號 標題」），h1 是內容區可以直接改的標題。
 * 欄位的編輯器直接用表格的格子，所以也放在同一個 Context 裡（專案內容、權限、今天）。
 * 下層項目的清單點了在旁邊開面板（ItemPanelHost，page 版面）；在面板裡刪除之後回到這一頁，提示放在內容最上面。
 */
export function ItemDetailView({ detail, item, notices }: ItemDetailViewProps) {
  const scope = useItemScope(detail);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const arrival = useArrivalNotice(headingRef);
  const projectLink = useProjectCrumbLink(item.projectKey);
  const entry: unknown = useLocation().state;
  // 窄螢幕頁列只留圖示（390px 寬時位置才不會被動作擠掉）；名稱、說明、做的事都一樣。
  const isCompact = useIsCompact();
  useRecordRecentItem(item);
  // 從清單連進來的：用上一頁回去時焦點放回清單上的這一筆。
  useEffect(() => {
    rememberReturnFocus(entry, item.key);
  }, [entry, item.key]);
  return (
    <ProjectTableProvider value={scope.value}>
      <PageLayout
        variant="reading"
        bar={
          <PageBar
            title={`${item.key} ${item.title}`}
            titleInBar={false}
            crumbs={itemCrumbs(item, detail.project.name, projectLink)}
            actions={
              <>
                <FollowButton itemKey={item.key} isCompact={isCompact} />
                <ShareItemButton item={item} isCompact={isCompact} />
                <ItemActions item={item} />
              </>
            }
          />
        }
      >
        <ItemPanelHost layout="page" projectKey={item.projectKey}>
          {notices}
          <ArchivedNotice detail={detail} />
          <UndoableArrival message={arrival} />
          <ItemArticle item={item} headingLevel={1} headingRef={headingRef} />
        </ItemPanelHost>
      </PageLayout>
      {scope.membersDialog}
    </ProjectTableProvider>
  );
}
