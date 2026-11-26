import { Link } from 'react-router';
import type { Item } from '../../api/types';
import { assigneeNote } from '../../domain/assigneeNote';
import { isOverdue } from '../../domain/dates';
import { cx } from '../../lib/cx';
import { useItemPanelLink } from '../item-page/ItemPanelContext';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import type { WeekSegment } from './calendarSpans';
import { useCalendarScope } from './calendarScope';
import styles from './Calendar.module.css';

/** 期間排法裡一段的長相：是不是條、兩端有沒有接續、在不在 Tab 的順序裡。 */
export type SegmentLook = Pick<WeekSegment, 'isBar' | 'continuesBefore' | 'continuesAfter' | 'isTabbable'>;

interface CalendarItemProps {
  readonly item: Item;
  /** 月曆的格子很窄：只顯示標題，編號放在名稱與 title。議程與浮層寬，照舊顯示編號。 */
  readonly isTitleOnly?: boolean;
  /** 期間排法裡跨天的項目的日期範圍（「11/2～11/6」）：寫進連結的名稱與 title；浮層裡也看得到。 */
  readonly range?: string | null;
  /** 月曆上的一段（期間排法）；不給就是一般的一行。 */
  readonly segment?: SegmentLook;
}

/**
 * 行事曆上的一個項目：連到項目頁的連結，一般的點擊在旁邊開項目面板（和看板的卡片一樣，useItemPanelLink）。
 * 名稱是「編號 標題」，跨天的後面接「，11/2～11/6」；過期的前面多一個「!」、標題用過期的顏色，並多一句「已過期」給報讀軟體與滑過的人
 * （顏色不是唯一的線索，強制色彩把顏色拿掉時「!」還在）。
 * isTitleOnly：月曆的格子只有 1/7 寬，編號佔掉一半、標題只剩幾個字；格子裡只畫標題，編號仍在連結的名稱與 title。
 * 條的兩端接續到上一週、下一週時畫「‹」「›」（aria-hidden：形狀就是線索，報讀軟體靠名稱裡的範圍）；
 * 同一個項目好幾段時只有第一段在 Tab 的順序裡，其餘 tabIndex=-1，報讀軟體與鍵盤不重複走同一個項目。
 */
export function CalendarItem({ item, isTitleOnly = false, range = null, segment }: CalendarItemProps) {
  const { todayDate, isAssigneeShown } = useCalendarScope();
  const panelLink = useItemPanelLink(item.key);
  // 刪除項目之後回到這裡（行事曆與它的條件）。
  const entry = useItemPageEntry();
  const overdue = isOverdue(item.dueDate, item.stateCategory, todayDate);
  const baseName = `${item.key} ${item.title}${range === null ? '' : `，${range}`}${isAssigneeShown ? `，${assigneeNote(item.assignee)}` : ''}`;
  const name = overdue ? `${baseName}（已過期）` : baseName;
  return (
    <Link
      to={itemPagePath(item.projectKey, item.key)}
      state={entry}
      className={cx(
        styles.item,
        overdue && styles.overdue,
        segment?.isBar === true && styles.spanBar,
        segment?.continuesBefore === true && styles.barOpenStart,
        segment?.continuesAfter === true && styles.barOpenEnd,
      )}
      title={name}
      // 範圍與負責人不在可見的內容裡（格子上只畫標題），名稱靠 aria-label 給完整的一句；兩者都沒有的照舊從內容串起來。
      aria-label={range === null && !isAssigneeShown ? undefined : name}
      tabIndex={segment?.isTabbable === false ? -1 : undefined}
      {...panelLink}
    >
      {segment?.continuesBefore === true && (
        <span className={styles.barMark} aria-hidden="true">
          ‹
        </span>
      )}
      {overdue && (
        <span className={styles.overdueMark} aria-hidden="true">
          !
        </span>
      )}
      <span className={cx(styles.itemKey, isTitleOnly && 'visually-hidden')}>{item.key}</span>
      <span className={styles.itemTitle}>{item.title}</span>
      {range !== null && !isTitleOnly && <span className={styles.itemRange}>{range}</span>}
      {segment?.continuesAfter === true && (
        <span className={styles.barMark} aria-hidden="true">
          ›
        </span>
      )}
      {overdue && <span className="visually-hidden">（已過期）</span>}
    </Link>
  );
}
