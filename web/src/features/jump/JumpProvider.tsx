import { createContext, use, useState, type ReactNode, type RefObject } from 'react';
import { useNavigate } from 'react-router';
import { SHORTCUTS, useShortcut } from '../../lib/shortcuts';
import { MembersDialog } from '../members/MembersDialog';
import { useFocusHeadingAfterNavigation } from './headingFocus';
import { JumpDialog } from './JumpDialog';
import type { JumpAction } from './jumpGroups';
import { useIsModalDialogOpen } from './useIsModalDialogOpen';

interface JumpControls {
  /** 打開快速跳轉（側欄的按鈕用）。 */
  readonly open: () => void;
}

const JumpContext = createContext<JumpControls | null>(null);

/** 側欄的「搜尋或跳到…」用：只能用在 JumpProvider 裡（外框）。 */
export function useJump(): JumpControls {
  const controls = use(JumpContext);
  if (controls === null) {
    throw new Error('useJump 只能用在 JumpProvider 裡。');
  }
  return controls;
}

interface JumpProviderProps {
  /** 主要內容區：換頁之後在這裡找新頁面的標題（h1）。 */
  readonly mainRef: RefObject<HTMLElement | null>;
  /**
   * 打開之前先做的事（外框用：窄螢幕的抽屜開著時先收起來、焦點交給「選單」）。
   * 對話框記下的「打開它的元素」是這之後有焦點的那一個，關上時焦點回到那裡。
   */
  readonly onOpen?: () => void;
  readonly children: ReactNode;
}

/**
 * 快速跳轉的開關（外框掛一次）：⌘K／Ctrl+K 與側欄的按鈕打開。別的對話框開著時 ⌘K 不作用（對話框上面不再開對話框）；
 * 自己開著時照樣收下，不交給瀏覽器（Windows 的 Chrome 會跳到網址列），焦點留在輸入框。
 * 選了一頁：先關上再換頁，焦點到新頁面的標題。選了「成員」：關上之後在原地打開成員對話框，關上它焦點回到原本的元素。
 * 沒有換頁就關上時，焦點回到打開它的元素（Dialog 自己做）。
 */
export function JumpProvider({ mainRef, onOpen, children }: JumpProviderProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [membersOf, setMembersOf] = useState<string | null>(null);
  const navigate = useNavigate();
  const focusHeadingAfterNavigation = useFocusHeadingAfterNavigation(mainRef);
  const isModalDialogOpen = useIsModalDialogOpen();
  const open = () => {
    if (isOpen) return;
    onOpen?.();
    setIsOpen(true);
  };
  useShortcut(SHORTCUTS.openJump, open, { isEnabled: isOpen || !isModalDialogOpen });

  function choose(action: JumpAction) {
    setIsOpen(false);
    if (action.kind === 'members') {
      setMembersOf(action.projectKey);
      return;
    }
    focusHeadingAfterNavigation();
    void navigate(action.to);
  }

  return (
    <JumpContext value={{ open }}>
      {children}
      {isOpen && (
        <JumpDialog
          onClose={() => {
            setIsOpen(false);
          }}
          onChoose={choose}
        />
      )}
      {membersOf !== null && (
        <MembersDialog
          projectKey={membersOf}
          onClose={() => {
            setMembersOf(null);
          }}
        />
      )}
    </JumpContext>
  );
}
