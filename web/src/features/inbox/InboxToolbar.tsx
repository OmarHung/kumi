import { useNavigate, useSearchParams } from 'react-router';
import { NOTIFICATION_SEARCH_MAX_LENGTH } from '../../api/notificationTypes';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { NavTabs } from '../../components/NavTabs/NavTabs';
import { SearchField } from '../item-table/FilterFields';
import { inboxFilterSearch, type InboxCategory, type InboxFilter } from './inboxFilter';
import styles from './Inbox.module.css';

const CATEGORIES: readonly { readonly category: InboxCategory; readonly label: string }[] = [
  { category: 'all', label: '全部' },
  { category: 'mentions', label: '提及與回覆' },
  { category: 'assigned', label: '指派給我' },
];

/** 分類分頁：換分頁時搜尋字、只看未讀與開著的面板 item 都留著。 */
export function InboxCategoryTabs({ filter }: { readonly filter: InboxFilter }) {
  const [searchParams] = useSearchParams();
  const tabs = CATEGORIES.map((entry) => ({
    key: entry.category,
    label: entry.label,
    to: { search: inboxFilterSearch(searchParams, { category: entry.category }) },
    isCurrent: entry.category === filter.category,
  }));
  return <NavTabs label="通知的分類" tabs={tabs} />;
}

/**
 * 搜尋框與「只看未讀」：兩個都只改網址（其他參數照樣帶著）。搜尋用 replace：邊打邊停下來的每一次不各留一筆瀏覽紀錄；
 * 切換是一個明確的動作，留一筆，上一頁回得去。
 */
export function InboxFilterControls({ filter }: { readonly filter: InboxFilter }) {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  return (
    <>
      <div className={styles.search}>
        <SearchField
          isToolbar
          label="搜尋通知"
          placeholder="人員、項目、專案或內容"
          maxLength={NOTIFICATION_SEARCH_MAX_LENGTH}
          value={filter.q}
          onSearch={(q) => {
            void navigate({ search: inboxFilterSearch(searchParams, { q }) }, { replace: true });
          }}
        />
      </div>
      <Button
        size="sm"
        variant="secondary"
        aria-pressed={filter.unreadOnly}
        className={styles.unreadToggle}
        onClick={() => {
          void navigate({ search: inboxFilterSearch(searchParams, { unreadOnly: !filter.unreadOnly }) });
        }}
      >
        {/* 勾只在按下時出現（篩選晶片的做法）：一直都在的勾讓沒按下的樣子看起來也像開著。 */}
        {filter.unreadOnly && <Icon name="check" />}
        只看未讀
      </Button>
    </>
  );
}
