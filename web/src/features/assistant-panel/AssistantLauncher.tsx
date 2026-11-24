import { useLayoutEffect, useRef, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { isNearDefaultPosition, useLauncherPosition, type LauncherPosition } from './launcherPosition';
import { setLauncherHidden, takeFocusRequest, useLauncherHidden } from './launcherPreference';
import { useLauncherAnchor } from './useLauncherAnchor';
import { useLauncherDrag } from './useLauncherDrag';
import styles from './AssistantPanel.module.css';

interface LauncherProps {
  readonly isShown: boolean;
  readonly onOpen: () => void;
  readonly buttonRef: RefObject<HTMLButtonElement | null>;
  /** 掛在哪個元素底下（useAssistantHost 的容器）。 */
  readonly host: HTMLElement;
}

/** 藏起來之後焦點到主要內容（沒有別的合理的地方：按鈕與「×」都不見了）。 */
function focusMain(): void {
  document.getElementById('main-content')?.focus({ preventScroll: true });
}

/**
 * 告訴新通知的提示讓出高度（根元素的 data-assistant="button"）：按鈕在畫面上、而且還在預設的右下角（48px 以內）時。
 * 搬走了就不讓：提示留在原本的右下角，不會疊在空的地方。從帳號選單叫回來或放回右下角：按鈕一出現就把焦點給它。
 */
function useLauncherEffects(
  buttonRef: RefObject<HTMLButtonElement | null>,
  { isVisible, isHidden, position, stored }: { isVisible: boolean; isHidden: boolean; position: LauncherPosition | null; stored: LauncherPosition | null },
): void {
  const isAtCorner = isNearDefaultPosition(position);
  useLayoutEffect(() => {
    if (!isVisible || !isAtCorner) return undefined;
    const root = document.documentElement;
    root.dataset.assistant = 'button';
    return () => {
      delete root.dataset.assistant;
    };
  }, [isAtCorner, isVisible]);
  useLayoutEffect(() => {
    if (!isHidden && takeFocusRequest()) buttonRef.current?.focus();
  }, [buttonRef, isHidden, stored]);
}

/**
 * 預設在畫面右下角、直接浮在頁面上的按鈕，按住可以拖到畫面上任何位置（規則在 docs/design/07-components.md 的 AssistantPanel）：
 * 移動超過 4px 才算拖曳，放開記住；放回右下角在帳號選單。右上角疊一顆小「×」可以藏起來：滑過或焦點在裡面時才出現，
 * 觸控裝置一直顯示；叫回來在左下角的帳號選單（「顯示 AI 助理按鈕」）。掛在 host 底下（項目的浮動視窗開著時在視窗裡）。
 */
export function AssistantLauncher({ isShown, onOpen, buttonRef, host }: LauncherProps) {
  const isHidden = useLauncherHidden();
  const stored = useLauncherPosition();
  const frameRef = useRef<HTMLDivElement>(null);
  const drag = useLauncherDrag(frameRef, stored);
  const position = drag.live ?? stored;
  useLauncherAnchor(isHidden ? null : position);
  useLauncherEffects(buttonRef, { isVisible: !isShown && !isHidden, isHidden, position, stored });
  if (isHidden) return null;
  return createPortal(
    <div ref={frameRef} className={styles.launcherFrame} hidden={isShown}>
      <button
        ref={buttonRef}
        type="button"
        className={styles.launcher}
        aria-label="AI 助理"
        title="AI 助理"
        aria-haspopup="dialog"
        aria-expanded={isShown}
        data-dragging={drag.isDragging}
        {...drag.handlers}
        onClick={() => {
          if (drag.consumeClick()) onOpen();
        }}
      >
        <Icon name="sparkles" />
      </button>
      <IconButton
        icon="close"
        label="隱藏 AI 助理按鈕"
        size="sm"
        variant="secondary"
        className={styles.hideLauncher}
        onClick={() => {
          setLauncherHidden(true);
          focusMain();
        }}
      />
    </div>,
    host,
  );
}
