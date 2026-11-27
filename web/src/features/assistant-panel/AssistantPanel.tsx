import { useEffect, useRef, useState, type RefObject } from 'react';
import { useLocation, useMatch } from 'react-router';
import type { AiOptions } from '../../api/aiTypes';
import { useAiOptions } from '../../api/assistant';
import { useBeforeUnloadWarning } from '../../lib/useBeforeUnloadWarning';
import { useIsCompact } from '../../lib/useMediaQuery';
import { panelItemOf } from '../work-items/itemLinks';
import { AssistantLauncher } from './AssistantLauncher';
import { PanelContent } from './PanelContent';
import { PanelHeader } from './PanelHeader';
import { PanelWindow } from './PanelWindow';
import { useAssistantHost } from './useAssistantHost';
import { OPEN_ASSISTANT_PANEL_EVENT, type OpenAssistantPanelDetail } from './panelRequests';
import { usePanelSession, type PanelSession } from './usePanelSession';
import styles from './AssistantPanel.module.css';

const INPUT_SELECTOR = 'textarea[aria-label="訊息"]';
const PROJECT_SELECT_SELECTOR = '[role="combobox"][aria-label="專案"]';
const TITLE_ID = 'assistant-panel-title';

/** 打開時焦點先給輸入框；沒有輸入框（還沒選專案、沒有能用的模型）給專案選單，再沒有給標題。調整大小的把手不算。 */
function focusTargetIn(panel: HTMLElement): HTMLElement | null {
  return (
    panel.querySelector<HTMLElement>(INPUT_SELECTOR) ??
    panel.querySelector<HTMLElement>(PROJECT_SELECT_SELECTOR) ??
    panel.querySelector<HTMLElement>(`#${TITLE_ID}`)
  );
}

/**
 * 打開時把焦點放進面板，內容變了（選了專案、選項讀進來，輸入框才出現）再試一次。
 * 裡面已經有人拿了焦點（例如剛用專案選單選了專案）就不搶；焦點在標題上（上一次的後備）算還沒找到。
 */
function useFocusOnOpen(isOpen: boolean, panelRef: RefObject<HTMLElement | null>, contentKey: string): void {
  useEffect(() => {
    const panel = panelRef.current;
    if (!isOpen || panel === null) return;
    const active = document.activeElement;
    const isLost = active === null || active === document.body || active === panel || active.id === TITLE_ID;
    if (!isLost && panel.contains(active)) return;
    focusTargetIn(panel)?.focus();
  }, [contentKey, isOpen, panelRef]);
}

interface PanelOpen {
  readonly isShown: boolean;
  readonly hasOpened: boolean;
  readonly open: () => void;
  readonly close: () => void;
}

/** 開關：按了才打開（第一次之後一直掛著、只是藏起來）；進了整頁版收起來；關上時焦點回到按鈕。 */
function usePanelOpen(isEntryShown: boolean, isFullPage: boolean, buttonRef: RefObject<HTMLButtonElement | null>): PanelOpen {
  const [isOpen, setIsOpen] = useState(false);
  const [hasOpened, setHasOpened] = useState(false);
  const restoreFocusRef = useRef(false);
  // 進了整頁版：面板收起來（離開整頁回來時不再自己彈出來）。
  if (isFullPage && isOpen) setIsOpen(false);
  const isShown = isOpen && isEntryShown && !isFullPage;
  if (isShown && !hasOpened) setHasOpened(true);
  useEffect(() => {
    if (!isShown && restoreFocusRef.current) {
      restoreFocusRef.current = false;
      buttonRef.current?.focus();
    }
  }, [buttonRef, isShown]);
  return {
    isShown,
    hasOpened,
    open: () => {
      setIsOpen(true);
    },
    close: () => {
      restoreFocusRef.current = true;
      setIsOpen(false);
    },
  };
}

interface WindowProps {
  readonly session: PanelSession;
  readonly options: AiOptions;
  readonly isFollowingRoute: boolean;
  readonly isShown: boolean;
  readonly isCompact: boolean;
  readonly onClose: () => void;
  readonly panelRef: RefObject<HTMLElement | null>;
  readonly host: HTMLElement;
}

/** 浮動的面板：外框、頁首與內容。 */
function AssistantWindow({ session, options, isFollowingRoute, isShown, isCompact, onClose, panelRef, host }: WindowProps) {
  useFocusOnOpen(isShown, panelRef, `${String(options.available)}|${String(session.projectKey)}`);
  return (
    <PanelWindow isOpen={isShown} isCompact={isCompact} onClose={onClose} panelRef={panelRef} host={host}>
      <PanelHeader
        projectKey={session.projectKey}
        isFollowingRoute={isFollowingRoute}
        isBusy={session.isBusy}
        onChoose={session.chooseProject}
        itemKey={session.itemKey}
        onChooseItem={session.chooseItem}
        conversationId={session.conversationId}
        onSelectConversation={session.selectConversation}
        onOpenConversation={session.openConversation}
        onClose={onClose}
      />
      <div className={styles.body}>
        <PanelContent session={session} options={options} />
      </div>
    </PanelWindow>
  );
}

/** 網址上開著的項目：項目頁，或清單旁邊開著的側邊面板（?item=）；沒有是 null。編號一律大寫。 */
function useRouteItemKey(): string | null {
  const { search } = useLocation();
  const onItemPage = useMatch({ path: '/projects/:projectKey/items/:itemKey' })?.params.itemKey;
  const key = onItemPage ?? panelItemOf(new URLSearchParams(search));
  return key === null ? null : key.toUpperCase();
}

/**
 * 對話裡的新專案建議卡請面板切到剛建好的專案並打開（panelRequests.ts）。
 * 回覆還在進行時不換專案（進行中的對話屬於原本的），只把面板打開。
 */
function useOpenRequests(session: PanelSession, open: () => void): void {
  const latest = useRef({ session, open });
  useEffect(() => {
    latest.current = { session, open };
  });
  useEffect(() => {
    const listen = (event: Event) => {
      const { projectKey } = (event as CustomEvent<OpenAssistantPanelDetail>).detail;
      const { session: current, open: show } = latest.current;
      if (!current.isBusy) current.chooseProject(projectKey);
      show();
    };
    window.addEventListener(OPEN_ASSISTANT_PANEL_EVENT, listen);
    return () => {
      window.removeEventListener(OPEN_ASSISTANT_PANEL_EVENT, listen);
    };
  }, []);
}

/**
 * 每一頁右下角的 AI 助理（internal/HANDOFF-ai-floating.md 5.1）：一顆按鈕，展開成浮動面板。
 * 掛在 AppShell 層：換頁不卸下，所以進行中的回覆不會被中斷；關掉面板也只是藏起來。
 * 組織有開放的模型、或允許自己接 AI 帳號時才有；整頁的 AI 助理（/projects/:key/assistant、/assistant）裡不顯示。
 * 同意頁與登入頁在 AppShell 外面，本來就沒有。
 */
export function AssistantPanel() {
  const options = useAiOptions();
  const routeProjectKey = useMatch({ path: '/projects/:projectKey', end: false })?.params.projectKey;
  const isProjectFullPage = useMatch({ path: '/projects/:projectKey/assistant', end: false }) !== null;
  const isOrganizationFullPage = useMatch({ path: '/assistant', end: false }) !== null;
  const isFullPage = isProjectFullPage || isOrganizationFullPage;
  const isCompact = useIsCompact();
  const routeItemKey = useRouteItemKey();
  const session = usePanelSession(routeProjectKey, routeItemKey);
  const hasEntry = options.data?.available === true || options.data?.canAddPersonal === true;
  const buttonRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLElement>(null);
  const panel = usePanelOpen(hasEntry, isFullPage, buttonRef);
  const host = useAssistantHost();
  useBeforeUnloadWarning(session.isBusy);
  useOpenRequests(session, panel.open);

  if (options.data === undefined || !hasEntry) return null;
  return (
    <>
      {!isFullPage && <AssistantLauncher isShown={panel.isShown} onOpen={panel.open} buttonRef={buttonRef} host={host} />}
      {panel.hasOpened && (
        <AssistantWindow
          session={session}
          options={options.data}
          // 在專案頁從對話選單開了別的專案的對話：面板不再跟著路由，頁首換回專案的選單，選得回來。
          // 回覆中換頁時面板留在原本的專案（回覆完才跟上），那段時間照舊只顯示名稱。
          isFollowingRoute={routeProjectKey !== undefined && (session.projectKey === routeProjectKey || session.isBusy)}
          isShown={panel.isShown}
          isCompact={isCompact}
          onClose={panel.close}
          panelRef={panelRef}
          host={host}
        />
      )}
    </>
  );
}
