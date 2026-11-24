import { useEffect, useState } from 'react';
import { useLocation, type Location } from 'react-router';
import { listNotifications } from '../../api/notifications';
import type { Notification } from '../../api/types';
import type { LiveNotificationHooks } from '../live/liveTypes';
import { isInboxPath, openItemKeysOf } from './openItem';
import { currentToastMode, useToastMode, type ToastMode } from './toastMode';
import { toastAnnouncement } from './toastText';
import { createToastWatcher } from './toastWatcher';

/** 讀最新的未讀：看提示的人只需要最新的幾則，一次一頁（合約的 limit 上限內）。 */
const UNREAD_LIMIT = 10;

export interface ToastAnnouncement {
  readonly text: string;
  readonly seq: number;
}

export interface NotificationToastsView {
  /** 還在畫面上的新通知，舊的在前。 */
  readonly entries: readonly Notification[];
  readonly mode: ToastMode;
  /** 報讀軟體要唸的一句；seq 每次有新的就加一（兩次的字一樣時也要再唸一次）。 */
  readonly announcement: ToastAnnouncement;
  readonly dismiss: (notificationIds: readonly string[]) => void;
}

export interface NotificationToasts {
  readonly view: NotificationToastsView;
  /** 交給即時連線：連上（含重連）時靜靜讀一次，收到 inbox 時檢查有沒有新的。 */
  readonly live: LiveNotificationHooks;
}

function canShowNow(location: Location): boolean {
  return currentToastMode() !== 'off' && document.visibilityState === 'visible' && !isInboxPath(location.pathname);
}

/** 最新的網址：即時連線的回呼在連線的那一側執行，要讀到的是當下的網址，不是建立時的。 */
function createLatest<T>(initial: T) {
  let value = initial;
  return {
    get: () => value,
    set: (next: T) => {
      value = next;
    },
  };
}

/**
 * 新通知的提示（internal/HANDOFF-notification-toasts.md）：登入後的外框用。
 * 位置（收件匣、開著的項目）記著最新的網址，觀察者在即時連線的回呼裡才讀它，不必因為換頁重新建立。
 */
export function useNotificationToasts(): NotificationToasts {
  const location = useLocation();
  const [latestLocation] = useState(() => createLatest(location));
  useEffect(() => {
    latestLocation.set(location);
  }, [latestLocation, location]);
  const mode = useToastMode();
  const [entries, setEntries] = useState<readonly Notification[]>([]);
  const [announcement, setAnnouncement] = useState<ToastAnnouncement>({ text: '', seq: 0 });
  // 改成「不顯示」就收掉還在畫面上的（渲染時調整，不必等 effect）。
  const [seenMode, setSeenMode] = useState(mode);
  if (mode !== seenMode) {
    setSeenMode(mode);
    if (mode === 'off') setEntries([]);
  }

  const [watcher] = useState(() =>
    createToastWatcher({
      fetchUnread: async () => (await listNotifications({ unread: true, limit: UNREAD_LIMIT })).items,
      canShow: () => canShowNow(latestLocation.get()),
      isItemOpen: (itemKey) => {
        const { pathname, search } = latestLocation.get();
        return openItemKeysOf(pathname, search).includes(itemKey.toUpperCase());
      },
      onFresh: (fresh) => {
        const shownMode = currentToastMode();
        setEntries((current) => [...current, ...fresh.filter((entry) => !current.some((known) => known.notificationId === entry.notificationId))]);
        const text = fresh.map((entry) => toastAnnouncement(entry, shownMode)).join('。');
        setAnnouncement((current) => ({ text, seq: current.seq + 1 }));
      },
    }),
  );
  useEffect(() => {
    watcher.setActive(true);
    return () => {
      watcher.setActive(false);
    };
  }, [watcher]);
  const [live] = useState<LiveNotificationHooks>(() => ({ onConnected: watcher.start, onInbox: watcher.check }));

  const [dismiss] = useState(() => (notificationIds: readonly string[]) => {
    setEntries((current) => current.filter((entry) => !notificationIds.includes(entry.notificationId)));
  });
  return { view: { entries, mode, announcement, dismiss }, live };
}
