import { use, useEffect, useLayoutEffect, useRef, type MouseEvent, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { Link } from 'react-router';
import { useMarkRead } from '../../api/notificationQueries';
import type { Notification } from '../../api/types';
import { INERT_EXEMPT_ATTRIBUTE } from '../../components/Dialog/inertExempt';
import { SUPPORTS_TOP_LAYER, useTopLayer } from '../../components/Floating/useTopLayer';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { isPlainLeftClick } from '../../components/SidePanel/plainClick';
import { isFocusLost } from '../../lib/updateThenFocus';
import { ItemPanelRegistryContext } from '../item-page/ItemPanelRegistry';
import { notificationLink } from '../inbox/notificationLink';
import { useItemPageEntry } from '../work-items/itemLinks';
import { useAutoDismiss } from './useAutoDismiss';
import { layoutToasts } from './toastText';
import { ToastContent } from './ToastContent';
import { useIsConfirmDialogOpen } from './useIsConfirmDialogOpen';
import { useToastHost } from './useToastHost';
import type { NotificationToastsView, ToastAnnouncement } from './useNotificationToasts';
import styles from './NotificationToasts.module.css';

const MIDDLE_BUTTON = 1;

/** 唸的字先清空、隔一下再填：連續兩則的字一樣時，內容有變化報讀軟體才會再唸一次。 */
const SPEAK_DELAY_MS = 100;

interface ToastShellProps {
  /** 這一則提示代表的通知編號（「還有 N 則」是好幾個）。 */
  readonly ids: readonly string[];
  /** 計時到了、Esc、關閉按鈕：焦點在這則裡面時要有去處。 */
  readonly onDismiss: (ids: readonly string[]) => void;
  readonly children: ReactNode;
}

/** 一則提示的外框：自己消失的計時（滑上去、焦點在裡面時暫停）、Esc 關掉、右上角的關閉。 */
function ToastShell({ ids, onDismiss, children }: ToastShellProps) {
  const pause = useAutoDismiss(() => {
    onDismiss(ids);
  });
  return (
    <div
      className={styles.toast}
      {...pause}
      onKeyDown={(event) => {
        if (event.key !== 'Escape') return;
        // 不讓同一個 Esc 再關掉後面的面板或對話框。
        event.stopPropagation();
        onDismiss(ids);
      }}
    >
      {children}
      <IconButton
        icon="close"
        size="sm"
        label="關閉這則通知"
        className={styles.close}
        onClick={() => {
          onDismiss(ids);
        }}
      />
    </div>
  );
}

interface NotificationToastProps {
  readonly notification: Notification;
  readonly mode: 'full' | 'compact';
  readonly onOpen: (notification: Notification) => void;
  readonly onDismiss: ToastShellProps['onDismiss'];
}

function NotificationToast({ notification, mode, onOpen, onDismiss }: NotificationToastProps) {
  const entry = useItemPageEntry();
  const panels = use(ItemPanelRegistryContext);
  const handleAuxClick = (event: MouseEvent<HTMLAnchorElement>) => {
    // 中鍵開新分頁：瀏覽器不送 click，只送 auxclick；看的是同一則，一樣標成已讀。
    if (event.button === MIDDLE_BUTTON) onOpen(notification);
  };
  return (
    <ToastShell ids={[notification.notificationId]} onDismiss={onDismiss}>
      <Link
        className={styles.link}
        to={notificationLink(notification)}
        state={entry}
        onClick={(event) => {
          // 和清單一樣：寬螢幕而且這一頁有項目面板時在旁邊的面板開（帶著留言的 hash，面板的時間軸捲過去）；
          // 窄螢幕、沒有面板的頁面、⌘／Ctrl 點與中鍵照連結換到項目頁。
          // 帳號通知沒有項目，只換頁到「命令列與整合」。
          const panel = panels?.current() ?? null;
          const { hash } = event.currentTarget;
          if (notification.kind !== 'account_security' && panel?.isEnabled === true && isPlainLeftClick(event)) {
            event.preventDefault();
            panel.open(notification.itemKey, hash);
          }
          onOpen(notification);
        }}
        onAuxClick={handleAuxClick}
      >
        <ToastContent notification={notification} mode={mode} />
      </Link>
    </ToastShell>
  );
}

interface FoldedToastProps {
  readonly folded: readonly Notification[];
  readonly allIds: readonly string[];
  readonly onDismiss: ToastShellProps['onDismiss'];
  /** 點了連結離開：換頁，焦點交給新的頁面。 */
  readonly onLeave: (ids: readonly string[]) => void;
}

/** 放不下的併成一則：連到收件匣，點了整堆都收掉（它們都在收件匣裡）。 */
function FoldedToast({ folded, allIds, onDismiss, onLeave }: FoldedToastProps) {
  return (
    <ToastShell ids={folded.map((entry) => entry.notificationId)} onDismiss={onDismiss}>
      <Link
        className={styles.link}
        to="/inbox"
        onClick={() => {
          onLeave(allIds);
        }}
      >
        <Icon name="notifications" className={styles.icon} />
        <span className={styles.body}>
          <span className={styles.headline}>還有 {folded.length} 則新通知</span>
        </span>
      </Link>
    </ToastShell>
  );
}

interface StackProps {
  readonly regionRef: RefObject<HTMLElement | null>;
  readonly entries: readonly Notification[];
  readonly mode: 'full' | 'compact';
  readonly onOpen: NotificationToastProps['onOpen'];
  readonly onDismiss: ToastShellProps['onDismiss'];
  readonly onLeave: FoldedToastProps['onLeave'];
}

function ToastStack({ regionRef, entries, mode, onOpen, onDismiss, onLeave }: StackProps) {
  // 在 top layer：項目面板、浮動視窗打開時也看得到、不被裁掉。不在每多一則時重新放回最上面：關上再打開會讓整疊提示重新滑入。
  useTopLayer(regionRef);
  const { visible, folded } = layoutToasts(entries);
  return (
    <section
      ref={regionRef}
      aria-label="新通知"
      popover={SUPPORTS_TOP_LAYER ? 'manual' : undefined}
      className={styles.region}
    >
      {folded.length > 0 && (
        // 數字變了就換一個 key：計時重新開始，不在使用者還沒看清楚之前消失。
        <FoldedToast key={folded.length} folded={folded} allIds={entries.map((entry) => entry.notificationId)} onDismiss={onDismiss} onLeave={onLeave} />
      )}
      {visible.map((notification) => (
        <NotificationToast key={notification.notificationId} notification={notification} mode={mode} onOpen={onOpen} onDismiss={onDismiss} />
      ))}
    </section>
  );
}

/**
 * 報讀的字放進一直在的 live region：沒有被擋住（確認類的對話框開著時先不唸，和畫面一樣等關掉再唸）時，
 * 先清空、隔 SPEAK_DELAY_MS 再填，連續兩則的字一樣也會再唸一次。直接寫 textContent（不經過 React 的 state）：
 * 「清空」是為了讓報讀軟體看到變化，不是畫面要顯示的狀態。
 */
function useSpoken(ref: RefObject<HTMLElement | null>, announcement: ToastAnnouncement, isBlocked: boolean): void {
  const { text, seq } = announcement;
  useEffect(() => {
    const node = ref.current;
    if (node === null) return undefined;
    node.textContent = '';
    if (isBlocked || text === '') return undefined;
    const timer = setTimeout(() => {
      node.textContent = text;
    }, SPEAK_DELAY_MS);
    return () => {
      clearTimeout(timer);
    };
  }, [ref, text, seq, isBlocked]);
}

interface NotificationToastsProps {
  readonly view: NotificationToastsView;
  /** false：點了不送「標成已讀」（展示頁的範例通知不是真的）。預設 true。 */
  readonly marksRead?: boolean;
}

/**
 * 新通知的提示（internal/HANDOFF-notification-toasts.md、docs/design/07-components.md）：右下角（窄螢幕在下方）疊成一欄，新的在下面。
 * 整層用 createPortal 放進 body，並帶 INERT_EXEMPT_ATTRIBUTE：項目的浮動視窗開著時背後是 inert，提示不受影響（點得到）。
 * 有 aria-modal 的視窗開著時改放進那個視窗的元素裡（useToastHost）：瀏覽器把視窗以外的東西從無障礙樹拿掉，放在外面唸不到。
 * 確認類的對話框開著時先不顯示（isBlocked），關掉再顯示：對話框很短，按鈕在右下會被蓋住。
 * 報讀用另一個一直在的 live region（polite、atomic，等於 role="status" 的行為）：提示本身出現時才掛上，掛上的內容報讀軟體不一定唸，所以唸的字另外放。
 * 不用 role="status" 本身：頁面上別的地方（送出結果、變更角色）的提示也是 status，測試與報讀都用角色找單一的那一個。
 */
export function NotificationToasts({ view, marksRead = true }: NotificationToastsProps) {
  const { entries, mode, announcement, dismiss } = view;
  const regionRef = useRef<HTMLElement>(null);
  const speakerRef = useRef<HTMLDivElement>(null);
  const needsFocus = useRef(false);
  const markRead = useMarkRead();
  const isBlocked = useIsConfirmDialogOpen();
  const host = useToastHost();
  useSpoken(speakerRef, announcement, isBlocked);

  const handleDismiss = (ids: readonly string[]) => {
    needsFocus.current = regionRef.current?.contains(document.activeElement) === true;
    dismiss(ids);
  };
  // 關掉提示、焦點跟著不見：放到下一則提示，沒有了就放到頁面的主要內容（不掉回頁面本體）。
  useLayoutEffect(() => {
    if (!needsFocus.current) return;
    needsFocus.current = false;
    if (!isFocusLost()) return;
    (regionRef.current?.querySelector<HTMLElement>('a, button') ?? document.getElementById('main-content'))?.focus();
  }, [entries]);

  const handleOpen = (notification: Notification) => {
    if (marksRead) markRead.mutate([notification.notificationId]);
    dismiss([notification.notificationId]);
  };

  return createPortal(
    <div {...{ [INERT_EXEMPT_ATTRIBUTE]: '' }} className={styles.layer}>
      <div ref={speakerRef} aria-live="polite" aria-atomic="true" className="visually-hidden" />
      {mode !== 'off' && entries.length > 0 && !isBlocked && (
        <ToastStack regionRef={regionRef} entries={entries} mode={mode} onOpen={handleOpen} onDismiss={handleDismiss} onLeave={dismiss} />
      )}
    </div>,
    host,
  );
}
