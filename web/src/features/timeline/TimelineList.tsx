import { Link } from 'react-router';
import type { ProjectBlueprint } from '../../api/blueprint';
import type { Item } from '../../api/types';
import { cx } from '../../lib/cx';
import { useItemSequence } from '../item-page/ItemPanelContext';
import { assigneeMeta, stateMeta } from '../work-items/itemListMeta';
import { itemPagePath, useItemPageEntry } from '../work-items/itemLinks';
import type { TimelineRow } from './timelineModel';
import { isTickUnderToday, todayCol, trackSegment, weekTicks } from './timelineTrack';
import { useTimelineTrack } from './TimelineTrackContext';
import { At } from './At';
import styles from './Timeline.module.css';

/** 軌道上的一段：顏色看狀態類別，過期的多一圈外框；日期的文字已經在連結的名稱裡，所以整條只是裝飾。 */
function Track({ row }: { readonly row: TimelineRow }) {
  const segment = trackSegment(row.placement);
  return (
    <div className={styles.lane} aria-hidden="true">
      <At
        col={segment.col}
        span={segment.span}
        className={cx(styles.trackBar, row.isOverdue && styles.trackOverdue)}
        attrs={{ 'data-category': row.item.stateCategory, 'data-flat-start': segment.isStartFlat || undefined, 'data-flat-end': segment.isEndFlat || undefined }}
      />
    </div>
  );
}

/** 控制列下面的週刻度加「今天」的膠囊：只是視覺，日期與過期都寫在每一筆的文字裡。 */
function Scale() {
  const { window, todayDate } = useTimelineTrack();
  const today = todayCol(window, todayDate);
  return (
    <div className={styles.scale} aria-hidden="true">
      {weekTicks(window).map((tick) => (
        <At key={tick.col} col={tick.col} className={styles.tick}>
          {today === null || !isTickUnderToday(tick.col, today) ? tick.label : ''}
        </At>
      ))}
      {today !== null && (
        <At col={today} className={styles.todayPill}>
          今天
        </At>
      )}
    </div>
  );
}

/** 從刻度列貫穿到最後一筆的今天的線。 */
function TodayLine() {
  const { window, todayDate } = useTimelineTrack();
  const today = todayCol(window, todayDate);
  if (today === null) return null;
  return (
    <div className={styles.todayLayer} aria-hidden="true">
      <At col={today} className={styles.todayLine} />
    </div>
  );
}

interface TrackRowProps {
  readonly row: TimelineRow;
  readonly blueprint: ProjectBlueprint | undefined;
  readonly showAssignee: boolean;
  readonly isContentStart: boolean;
}

function TrackRow({ row, blueprint, showAssignee, isContentStart }: TrackRowProps) {
  const entry = useItemPageEntry();
  const { item } = row;
  return (
    <li className={styles.trackItem} data-content-start={isContentStart || undefined}>
      <Link to={itemPagePath(item.projectKey, item.key)} state={entry} className={styles.trackLink}>
        <span className={styles.trackHead}>
          <span className={styles.trackCode}>{item.key}</span>{' '}
          <span className={styles.trackTitle} title={item.title}>
            {item.title}
          </span>
        </span>{' '}
        <span className={styles.trackMeta}>
          {stateMeta(item, blueprint).content}{' '}
          <span className={styles.trackDates}>
            <span className="visually-hidden">期間：</span>
            {row.range}
            {row.isOverdue && <span className={styles.trackOverdueText}>{' · 已過期'}</span>}
          </span>
          {showAssignee && (
            <>
              {' '}
              <span className={styles.trackAssignee}>{assigneeMeta(item).content}</span>
            </>
          )}
        </span>
        <Track row={row} />
      </Link>
    </li>
  );
}

/**
 * 窄螢幕取代時間軸的清單（docs/design/08-interaction.md「時間軸」）：照開始日排，每一筆一條軌道，
 * 軌道上的實心條畫出項目在這個範圍裡從哪天到哪天；上面是週的刻度，今天的位置一條線貫穿到最後一筆。
 * 不橫向捲、不用手指點一天寬的條：整筆是連結，日期也寫成文字。窄螢幕不用面板：連結進整頁（和表格、看板一樣）。
 * 狀態的名稱照項目的專案藍圖（blueprintOf）：專案頁都是同一個，「我的工作」是每個項目自己的；還沒讀到時用類別的名稱。
 */
export function TimelineList({ rows, blueprintOf, showAssignee = false }: { readonly rows: readonly TimelineRow[]; readonly blueprintOf: (item: Item) => ProjectBlueprint | undefined; readonly showAssignee?: boolean }) {
  const { window } = useTimelineTrack();
  useItemSequence(rows.map((row) => row.item.key));
  return (
    <div className={styles.trackFrame}>
      <At days={window.days} className={styles.trackBody}>
        <Scale />
        <ul className={styles.trackList} aria-label="項目">
          {rows.map((row, index) => (
            <TrackRow key={row.item.key} row={row} blueprint={blueprintOf(row.item)} showAssignee={showAssignee} isContentStart={index === 0} />
          ))}
        </ul>
        <TodayLine />
      </At>
    </div>
  );
}
