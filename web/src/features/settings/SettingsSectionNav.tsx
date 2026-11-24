import { Fragment, type MouseEvent } from 'react';
import { Link, useParams } from 'react-router';
import { NavTabs, type NavTab, type NavTabCount } from '../../components/NavTabs/NavTabs';
import { Toolbar } from '../../components/Toolbar/Toolbar';
import { SETTINGS_SECTIONS, settingsSectionPath, type SettingsSection } from '../work-items/itemLinks';
import { SECTION_LABELS, type SectionCounts } from './settingsSections';
import styles from './Settings.module.css';

/** 每個區塊的兩個數字：還沒送出的修改、要修正的問題。 */
export interface SectionTallies {
  readonly changes: SectionCounts;
  readonly issues: SectionCounts;
}

/**
 * 區塊的網址用網址上的專案代號（不是專案內容裡的）：寫法不同時（大小寫）換區塊也還是同一個專案的設定頁，
 * 不會因為代號換了寫法而重新讀專案、丟掉草稿。
 */
function useSectionPath(): (section: SettingsSection) => string {
  const { projectKey = '' } = useParams();
  return (section) => settingsSectionPath(projectKey, section);
}

/** 是 0 的不顯示：每個分頁都掛著「0」只是雜訊。完整的說法給報讀軟體（NavTabs 用 aria-describedby 連到分頁）。 */
function tabCounts(changes: number, issues: number): readonly NavTabCount[] {
  const pending: readonly NavTabCount[] = changes > 0 ? [{ value: changes, label: `${String(changes)} 項還沒送出的修改`, tone: 'neutral' }] : [];
  const problems: readonly NavTabCount[] = issues > 0 ? [{ value: issues, label: `${String(issues)} 個要修正的問題`, tone: 'danger' }] : [];
  return [...pending, ...problems];
}

interface SettingsTabsProps {
  readonly section: SettingsSection;
  readonly tallies: SectionTallies;
}

/**
 * 區塊的導覽（工具列上的 NavTabs，docs/DESIGN.md 6.1）。分頁在很窄的螢幕上放不下時（每個都帶兩個數字），
 * 在自己的容器裡橫向捲動，不把整頁撐寬。
 */
export function SettingsTabs({ section, tallies }: SettingsTabsProps) {
  const pathOf = useSectionPath();
  const tabs: readonly NavTab[] = SETTINGS_SECTIONS.map((key) => ({
    key,
    label: SECTION_LABELS[key],
    to: pathOf(key),
    isCurrent: key === section,
    counts: tabCounts(tallies.changes[key], tallies.issues[key]),
  }));
  return (
    <Toolbar>
      <div className={styles.sectionNav}>
        <NavTabs label="設定的區塊" tabs={tabs} />
      </div>
    </Toolbar>
  );
}

/** 沒按修飾鍵的左鍵：只有這種點擊會在這一頁換區塊（其他的交給瀏覽器開新分頁、新視窗）。 */
function isPlainClick(event: MouseEvent): boolean {
  return event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey;
}

interface ElsewhereIssuesProps {
  readonly section: SettingsSection;
  readonly issues: SectionCounts;
  /** 點了某個區塊：換過去之後焦點放到那個區塊的標題（這個連結會跟著不見）。 */
  readonly onGo: (section: SettingsSection) => void;
}

/**
 * 送出前的檢查或伺服器的問題落在目前沒有畫出來的區塊時，送出列說是哪幾個區塊、各幾個，並連過去：
 * 「別的區塊也有要修正的地方：流程 2 個、欄位 1 個。」不是即時區域：上面的 alert 已經唸過，改到一半數字變了不必再唸。
 */
export function ElsewhereIssues({ section, issues, onGo }: ElsewhereIssuesProps) {
  const pathOf = useSectionPath();
  const others = SETTINGS_SECTIONS.filter((key) => key !== section && issues[key] > 0);
  if (others.length === 0) {
    return null;
  }
  return (
    <p className={styles.elsewhere}>
      別的區塊也有要修正的地方：
      {others.map((key, index) => (
        <Fragment key={key}>
          {index > 0 && '、'}
          <Link
            to={pathOf(key)}
            onClick={(event) => {
              if (isPlainClick(event)) onGo(key);
            }}
          >
            {`${SECTION_LABELS[key]} ${String(issues[key])} 個`}
          </Link>
        </Fragment>
      ))}
      。
    </p>
  );
}
