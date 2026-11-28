import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Icon, type IconName } from '../../../components/Icon/Icon';
import { PRODUCT_NAME } from '../../../config/product';
import { Marker, UiButton } from '../HelpVisuals';
import styles from './QuickStartFigures.module.css';

/*
 * 快速上手的示意圖：用真的圖示與介面的形狀畫出「在畫面的哪裡」，不是截圖（截圖會帶到真的資料，畫面一改就過時）。
 * 示意框本身 aria-hidden，圖例（有序清單）才是內容，編號對得上。
 */

interface MockNavItem {
  readonly icon: IconName;
  readonly label: string;
  readonly marker?: number;
}

const MOCK_NAV: readonly MockNavItem[] = [
  { icon: 'myWork', label: '我的工作', marker: 2 },
  { icon: 'overview', label: '專案總覽' },
  { icon: 'notifications', label: '收件匣', marker: 3 },
  { icon: 'knowledge', label: '知識庫' },
  { icon: 'projects', label: '專案', marker: 4 },
  { icon: 'settings', label: '設定' },
];

function MockSidebar() {
  return (
    <div className={styles.mockSidebar}>
      <span className={styles.mockBrand}>{PRODUCT_NAME}</span>
      <span className={styles.mockSearch}>
        <Icon name="search" />
        <span className={styles.mockLabel}>搜尋</span>
        <Marker n={1} />
      </span>
      {MOCK_NAV.map((item) => (
        <span key={item.label} className={styles.mockNavItem}>
          <Icon name={item.icon} />
          <span className={styles.mockLabel}>{item.label}</span>
          {item.marker !== undefined && <Marker n={item.marker} />}
        </span>
      ))}
    </div>
  );
}

function MockMain() {
  return (
    <div className={styles.mockMain}>
      <span className={styles.mockBar}>
        <span className={styles.mockLine} data-width="short" />
      </span>
      <span className={styles.mockContent}>
        <span className={styles.mockLine} />
        <span className={styles.mockLine} data-width="long" />
        <span className={styles.mockLine} />
        <span className={styles.mockLine} data-width="short" />
      </span>
      <span className={styles.mockAssistant}>
        <Marker n={5} />
        <span className={styles.mockFab}>
          <Icon name="sparkles" />
        </span>
      </span>
    </div>
  );
}

interface LegendItem {
  readonly n: number;
  readonly title: ReactNode;
  readonly text: ReactNode;
}

const LEGEND: readonly LegendItem[] = [
  { n: 1, title: '搜尋或跳到…', text: <>打編號或標題直接過去，<kbd>⌘K</kbd>／<kbd>Ctrl+K</kbd></> },
  { n: 2, title: <Link to="/my-work">我的工作</Link>, text: '所有專案裡你負責的' },
  { n: 3, title: <Link to="/inbox">收件匣</Link>, text: '指派、提及、回覆' },
  { n: 4, title: <Link to="/projects">專案</Link>, text: '點一個進去' },
  { n: 5, title: 'AI 助理', text: '幫你建任務、查進度' },
];

/** 「認識畫面」：側欄、內容、右下角的 AI 助理，加編號的圖例。 */
export function ScreenMap() {
  return (
    <figure className={styles.figure} aria-label="畫面的示意圖">
      <div className={styles.frame} aria-hidden="true">
        <MockSidebar />
        <MockMain />
      </div>
      <figcaption>
        <ol className={styles.legend}>
          {LEGEND.map((item) => (
            <li key={item.n}>
              <Marker n={item.n} />
              <span>
                <strong>{item.title}</strong>
                <span className={styles.legendText}>{item.text}</span>
              </span>
            </li>
          ))}
        </ol>
      </figcaption>
    </figure>
  );
}

const PROJECT_ACTIONS: readonly { readonly icon: IconName; readonly label: string }[] = [
  { icon: 'sparkles', label: 'AI 助理' },
  { icon: 'info', label: '資訊' },
  { icon: 'knowledge', label: '知識庫' },
  { icon: 'settings', label: '設定' },
  { icon: 'calendar', label: '規劃' },
  { icon: 'members', label: '成員' },
];

/** 「進入專案」：專案頁的頁列與版面分頁長什麼樣子。 */
export function ProjectBarMock() {
  return (
    <figure className={styles.figure} aria-label="專案頁上方的示意圖">
      <div className={styles.projectFrame} aria-hidden="true">
        <span className={styles.projectTitle}>
          <span className={styles.projectKey}>WEB</span>官網改版
        </span>
        <span className={styles.projectActions}>
          {PROJECT_ACTIONS.map((action) => (
            <span key={action.label} className={styles.projectAction}>
              <Icon name={action.icon} />
              {action.label}
            </span>
          ))}
          <Icon name="more" />
        </span>
        <span className={styles.projectTabs}>
          <span className={styles.projectTab} data-current="true">
            <Icon name="table" />
            表格
          </span>
          <span className={styles.projectTab}>
            <Icon name="board" />
            看板
          </span>
          <span className={styles.projectTab}>
            <Icon name="calendar" />
            行事曆
          </span>
          <span className={styles.projectTab}>
            <Icon name="timeline" />
            時間軸
          </span>
        </span>
      </div>
    </figure>
  );
}

/** 「AI 助理」：建議卡的樣子——列出要改什麼，按「套用」才會改。 */
export function ProposalMock() {
  return (
    <figure className={styles.figure} aria-label="AI 助理的建議卡示意圖：列出要改什麼，下面是「套用」與「不要」">
      <div className={styles.proposal} aria-hidden="true">
        <span className={styles.proposalHead}>
          <Icon name="sparkles" />
          修改建議 · <span className={styles.projectKey}>WEB-12</span> 首頁改版
        </span>
        <span className={styles.proposalRow}>
          <span className={styles.proposalField}>狀態</span>
          <span className={styles.proposalOld}>待處理</span>
          <Icon name="chevronRight" />
          <span>進行中</span>
        </span>
        <span className={styles.proposalRow}>
          <span className={styles.proposalField}>留言</span>
          <span>我明天開始做</span>
        </span>
        <span className={styles.proposalActions}>
          <UiButton tone="primary">套用</UiButton>
          <UiButton>不要</UiButton>
        </span>
      </div>
    </figure>
  );
}
