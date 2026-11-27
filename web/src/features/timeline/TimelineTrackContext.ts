import { createContext, useContext } from 'react';
import type { CalendarDate } from '../../api/types';
import type { TimelineWindow } from './timelineRange';

export interface TimelineTrackContextValue {
  readonly window: TimelineWindow;
  readonly todayDate: CalendarDate;
}

/**
 * 窄螢幕的清單要知道畫面的範圍與今天才畫得出軌道：由 TimelineFrame 提供，
 * 專案與「我的工作」各自傳進來的 renderList 就不必多帶參數。
 */
export const TimelineTrackContext = createContext<TimelineTrackContextValue | null>(null);

export function useTimelineTrack(): TimelineTrackContextValue {
  const value = useContext(TimelineTrackContext);
  if (value === null) throw new Error('TimelineList 要放在 TimelineFrame 裡面');
  return value;
}
