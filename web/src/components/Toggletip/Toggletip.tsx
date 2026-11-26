import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type PointerEvent, type RefObject } from 'react';
import { buttonClassName } from '../Button/Button';
import { Floating } from '../Floating/Floating';
import { Icon, type IconName } from '../Icon/Icon';
import styles from './Toggletip.module.css';

/** 滑鼠離開後等這麼久才收起來：從按鈕移進浮出來的框裡的這段路上不會閃掉（WCAG 1.4.13 能停在上面）。 */
const HOVER_CLOSE_DELAY_MS = 150;

export interface ToggletipProps {
  readonly icon: IconName;
  /** 這個圖示代表什麼（「好處」）：名稱是「好處：說明」。 */
  readonly label: string;
  readonly text: string;
  /** 圖示的顏色有意義時給（好處用 success、代價用 warning）；文字照樣說清楚，不只靠顏色。 */
  readonly tone?: 'success' | 'warning';
}

/** 開關：pinned 是點了打開的（點外面或再點一次才收），其餘是滑過或取得焦點時暫時打開的。 */
function useToggletipState() {
  const [state, setState] = useState<'closed' | 'peek' | 'pinned'>('closed');
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const cancelClose = useCallback(() => {
    if (closeTimer.current !== null) clearTimeout(closeTimer.current);
    closeTimer.current = null;
  }, []);
  useEffect(() => cancelClose, [cancelClose]);
  const close = useCallback(() => {
    cancelClose();
    setState('closed');
  }, [cancelClose]);
  return {
    state,
    close,
    cancelClose,
    peek: () => {
      cancelClose();
      setState((current) => (current === 'pinned' ? current : 'peek'));
    },
    closeLater: () => {
      cancelClose();
      closeTimer.current = setTimeout(() => {
        setState((current) => (current === 'peek' ? 'closed' : current));
      }, HOVER_CLOSE_DELAY_MS);
    },
    toggle: () => {
      cancelClose();
      setState((current) => (current === 'pinned' ? 'closed' : 'pinned'));
    },
  };
}

/** 點了打開之後，按在按鈕與浮出來的框以外的地方就收起來。 */
function useCloseOnOutsidePointer(isPinned: boolean, rootRef: RefObject<HTMLElement | null>, close: () => void) {
  useEffect(() => {
    if (!isPinned) return undefined;
    const handle = (event: globalThis.PointerEvent) => {
      if (event.target instanceof Node && rootRef.current?.contains(event.target)) return;
      close();
    };
    document.addEventListener('pointerdown', handle);
    return () => {
      document.removeEventListener('pointerdown', handle);
    };
  }, [isPinned, rootRef, close]);
}

const isMouse = (event: PointerEvent) => event.pointerType === 'mouse';

/**
 * 只有圖示的說明提示（docs/design/07-components.md 的 Toggletip）：滑鼠滑過、鍵盤移到上面時浮出一句說明，觸控時點一下打開、再點一下收起。
 * 規範第 9 節「不只靠滑過才出現」：所以焦點與點擊都打得開，Esc 收起。整句說明就是按鈕的名稱，報讀軟體不必打開也唸得到；
 * 浮出來的框只給看的人（aria-hidden），不會唸兩次。
 */
export function Toggletip({ icon, label, text, tone }: ToggletipProps) {
  const tip = useToggletipState();
  const rootRef = useRef<HTMLSpanElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  useCloseOnOutsidePointer(tip.state === 'pinned', rootRef, tip.close);
  function handleKeyDown(event: KeyboardEvent) {
    if (event.key !== 'Escape' || tip.state === 'closed') return;
    event.stopPropagation();
    tip.close();
  }
  return (
    <span ref={rootRef} className={styles.root}>
      <button
        ref={triggerRef}
        type="button"
        className={buttonClassName('ghost', 'sm', styles.trigger)}
        data-tone={tone}
        aria-label={`${label}：${text}`}
        onPointerEnter={(event) => {
          if (isMouse(event)) tip.peek();
        }}
        onPointerLeave={(event) => {
          if (isMouse(event)) tip.closeLater();
        }}
        onFocus={tip.peek}
        onBlur={tip.close}
        onClick={tip.toggle}
        onKeyDown={handleKeyDown}
      >
        <Icon name={icon} />
      </button>
      {tip.state !== 'closed' && (
        <Floating anchorRef={triggerRef} align="start" onAnchorHidden={tip.close} keepsAnchorFocus className={styles.surface}>
          <span aria-hidden="true" className={styles.bubble} onPointerEnter={tip.cancelClose} onPointerLeave={tip.closeLater}>
            <span className={styles.bubbleLabel}>{label}：</span>
            {text}
          </span>
        </Floating>
      )}
    </span>
  );
}
