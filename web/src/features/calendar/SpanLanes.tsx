import type { WeekSegment } from './calendarSpans';
import { CalendarItem } from './CalendarItem';
import styles from './Calendar.module.css';

/**
 * 期間排法：一週的條。放在這一週第一天的格子裡、鋪滿七天（絕對定位）：<table> 的格子不能跨列、也沒辦法讓一個元素跨好幾格，
 * 條要橫跨好幾天只能疊在格子上面。位置只靠 data-col、data-span、data-lane（樣式表依這三個屬性設 --col、--span、--lane，
 * 不寫行內樣式，第 13 節）；覆蓋層本身不接滑鼠，只有條接，格子底下的「還有 N 個」照樣點得到。
 */
export function SpanLanes({ segments }: { readonly segments: readonly WeekSegment[] }) {
  return (
    <ul className={styles.spanLanes}>
      {segments.map((segment) => (
        <li
          key={`${segment.item.key}-${String(segment.col)}`}
          className={styles.spanLane}
          data-col={segment.col}
          data-span={segment.length}
          data-lane={segment.lane}
        >
          <CalendarItem item={segment.item} isTitleOnly range={segment.range} segment={segment} />
        </li>
      ))}
    </ul>
  );
}
