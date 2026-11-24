import { useEffect, useState, type ReactNode, type Ref } from 'react';
import { useItem, useProject } from '../../api/queries';
import type { ItemDetail, ProjectDetail } from '../../api/types';
import { RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { today } from '../../domain/dates';
import { abilitiesOf, type ProjectTableContextValue } from '../item-table/ProjectTableContext';
import { recordRecentItem } from '../jump/recentItems';
import { MembersDialog } from '../members/MembersDialog';
import { AttachmentSection } from '../attachments/AttachmentSection';
import { ActivitySection } from './ActivitySection';
import { ChildItems } from './ChildItems';
import { DescriptionSection } from './DescriptionSection';
import { ItemFieldsPanel } from './ItemFieldsPanel';
import { ItemHeader, type ItemHeadingLevel } from './ItemHeader';
import styles from './ItemPage.module.css';

/**
 * 項目頁與項目面板共用的內容（docs/DESIGN.md 8.3「項目面板」：面板的內容就是項目頁的內容）：
 * 讀項目與專案（藍圖、成員、權限），分出找不到、沒有權限、讀不到與載入好，各自決定怎麼畫（整頁或面板裡）。
 */
export type ItemContentState =
  | { readonly status: 'loading' }
  | { readonly status: 'itemError'; readonly error: unknown; readonly retry: () => void }
  | { readonly status: 'projectError'; readonly projectKey: string; readonly error: unknown; readonly retry: () => void }
  /** 網址的專案或編號的寫法和項目對不上：換到正規的寫法（整頁換網址、面板換 item 參數）。 */
  | { readonly status: 'misplaced'; readonly item: ItemDetail }
  | { readonly status: 'ready'; readonly item: ItemDetail; readonly detail: ProjectDetail; readonly notices: ReactNode };

export const HTTP_FORBIDDEN = 403;
export const HTTP_NOT_FOUND = 404;

/** 找不到、沒有權限看的說明：整頁與面板用同一套文字。 */
export const ITEM_NOT_FOUND = {
  title: '找不到這個項目',
  message: (itemKey: string) => `沒有編號是 ${itemKey} 的項目，或是你沒有權限看它。`,
};
export const ITEM_FORBIDDEN = {
  title: '沒有權限看這個項目',
  message: '你在這個專案的角色不能看項目。需要的話，請專案的管理員調整你的角色。',
};

const STALE_ITEM = '無法更新這個項目的最新資料，畫面上可能是舊的。';
const STALE_PROJECT = '無法更新專案的最新資料（欄位、成員與權限），畫面上可能是舊的。';

/**
 * followItem：專案以項目自己的為準（面板：我的工作跨專案，網址上只有編號）；整頁照網址的專案讀，對不上就是 misplaced。
 * 只有第一次就讀不到（還沒有資料）才是錯誤；已經有資料時重抓失敗，留著內容，notices 多一行提示。
 */
export function useItemContent(itemKey: string, projectKey: string, followItem = false): ItemContentState {
  const item = useItem(itemKey);
  const actualProjectKey = followItem ? (item.data?.projectKey ?? projectKey) : projectKey;
  const project = useProject(actualProjectKey);
  if (item.isLoadingError) {
    const retry = () => {
      void item.refetch();
    };
    return { status: 'itemError', error: item.error, retry };
  }
  if (item.data !== undefined && (item.data.projectKey !== actualProjectKey || item.data.key !== itemKey)) {
    return { status: 'misplaced', item: item.data };
  }
  if (project.isLoadingError) {
    const retry = () => {
      void project.refetch();
    };
    return { status: 'projectError', projectKey: actualProjectKey, error: project.error, retry };
  }
  if (item.data === undefined || project.data === undefined) {
    return { status: 'loading' };
  }
  const notices = (
    <>
      <RefetchErrorNotice query={item} message={STALE_ITEM} />
      <RefetchErrorNotice query={project} message={STALE_PROJECT} />
    </>
  );
  return { status: 'ready', item: item.data, detail: project.data, notices };
}

export interface ItemScope {
  /** 給 ProjectTableProvider：欄位的編輯器就是表格的格子，要專案內容、權限與「今天」。還沒讀到專案時是 null。 */
  readonly value: ProjectTableContextValue | null;
  /** 改狀態被角色擋下時「到『成員』調整角色」打開的對話框（沒開時是 false）。 */
  readonly membersDialog: ReactNode;
}

/** 項目內容外面那一層：專案的 Context 與成員對話框。整頁與面板各一份（面板的專案可以和清單的不同）。 */
export function useItemScope(detail: ProjectDetail): ItemScope & { readonly value: ProjectTableContextValue };
export function useItemScope(detail: ProjectDetail | undefined): ItemScope;
export function useItemScope(detail: ProjectDetail | undefined): ItemScope {
  const [isMembersOpen, setIsMembersOpen] = useState(false);
  const [todayDate] = useState(today);
  if (detail === undefined) {
    return { value: null, membersDialog: false };
  }
  const value = {
    detail,
    can: abilitiesOf(detail),
    todayDate,
    onOpenMembers: () => {
      setIsMembersOpen(true);
    },
  };
  const membersDialog = isMembersOpen && (
    <MembersDialog
      projectKey={detail.project.key}
      onClose={() => {
        setIsMembersOpen(false);
      }}
    />
  );
  return { value, membersDialog };
}

/** 顯示一個項目時記進「最近看過的項目」（快速跳轉用）；標題改了也更新。 */
export function useRecordRecentItem(item: ItemDetail | undefined): void {
  const key = item?.key;
  const projectKey = item?.projectKey;
  const title = item?.title;
  useEffect(() => {
    if (key === undefined || projectKey === undefined || title === undefined) return;
    recordRecentItem({ key, projectKey, title });
  }, [key, projectKey, title]);
}

interface ItemArticleProps {
  readonly item: ItemDetail;
  /** 整頁是 1；面板裡是 2（面板的標題列已經是 h2 的區域，一頁不能有兩個 h1）。 */
  readonly headingLevel: ItemHeadingLevel;
  /** 換頁後帶著提示到達時，焦點放到標題（整頁才用）。 */
  readonly headingRef?: Ref<HTMLHeadingElement>;
}

/**
 * 項目的內容（閱讀頁的內容區）：標題區、欄位、說明、附件、下層項目、活動與留言。
 * 原始碼的順序是標題、欄位、內容：窄的時候上下排，狀態與負責人緊接在標題下面，不必捲過整條時間軸。
 * 兩欄與否看容器的寬度（ItemPage.module.css 的 item-page），面板窄就自己排成一欄。
 */
export function ItemArticle({ item, headingLevel, headingRef }: ItemArticleProps) {
  return (
    <article className={styles.page}>
      <ItemHeader item={item} headingLevel={headingLevel} headingRef={headingRef} />
      <div className={styles.layout}>
        <ItemFieldsPanel item={item} />
        <div className={styles.main}>
          <DescriptionSection item={item} />
          <AttachmentSection key={item.key} item={item} />
          <ChildItems item={item} />
          <ActivitySection item={item} />
        </div>
      </div>
    </article>
  );
}
