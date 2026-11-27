import { useEffect, useState, type RefObject } from 'react';
import { useLocation } from 'react-router';

interface DrawerOptions {
  /** 開關按鈕：用鍵盤關掉抽屜時，焦點回到這裡（寬螢幕疊在頁列上的「選單」）。 */
  readonly buttonRef: RefObject<HTMLButtonElement | null>;
  /** 窄螢幕的開關是底部分頁列的「更多」（「選單」不顯示）：窄螢幕時焦點回到這裡。 */
  readonly compactButtonRef: RefObject<HTMLButtonElement | null>;
  /** 抽屜本身：打開時把焦點移進第一個導覽連結。 */
  readonly drawerRef: RefObject<HTMLElement | null>;
  /** 現在是不是窄螢幕（useIsCompact）：從窄變寬時抽屜關上。 */
  readonly isCompact: boolean;
}

/** 窄螢幕側欄抽屜的開關、焦點與換頁、變寬時自動關閉。 */
export function useDrawer({ buttonRef, compactButtonRef, drawerRef, isCompact }: DrawerOptions) {
  const location = useLocation();
  // 記下打開時的那一次導覽；換頁之後 key 不同，抽屜自然就關了，不必用 effect 去關。
  const [openedAt, setOpenedAt] = useState<string | null>(null);
  // 視窗從窄變寬：寬螢幕的側欄不是抽屜，記著「開著」的話，再變窄時會自己跳出來，按鈕也一直說展開。
  // 看的是「變寬的那一下」而不是「現在寬不寬」：沒有 matchMedia 的環境（測試）一律當成寬螢幕，抽屜照樣要能開。
  const [wasCompact, setWasCompact] = useState(isCompact);
  if (wasCompact !== isCompact) {
    setWasCompact(isCompact);
    if (!isCompact) setOpenedAt(null);
  }
  const isOpen = openedAt === location.key;

  // 抽屜打開後才看得見、才能聚焦，所以要等畫面更新完再把焦點移進去。
  useEffect(() => {
    if (isOpen) {
      drawerRef.current?.querySelector<HTMLElement>('nav a')?.focus();
    }
  }, [isOpen, drawerRef]);

  return {
    isOpen,
    toggle: () => {
      setOpenedAt(isOpen ? null : location.key);
    },
    close: () => {
      setOpenedAt(null);
    },
    closeAndRefocus: () => {
      setOpenedAt(null);
      (isCompact ? compactButtonRef : buttonRef).current?.focus();
    },
  };
}
