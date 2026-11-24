import { useId, useRef } from 'react';
import { SHORTCUTS, useShortcut } from '../../lib/shortcuts';
import { ModalFrame } from '../Dialog/ModalFrame';
import styles from './FloatingPanel.module.css';
import { FloatingPanelResizer } from './FloatingPanelResizer';
import { PanelHeader, useHeadingFocus, type SidePanelProps } from './SidePanel';
import panelStyles from './SidePanel.module.css';
import { useFloatingPanelWidth } from './useFloatingPanelWidth';

/** 項目的浮動視窗和側邊面板收一樣的東西（名稱、標題、動作、關閉、標題的 ref、內容）。 */
export type FloatingPanelProps = SidePanelProps;

/**
 * 項目面板的另一種樣子（docs/DESIGN.md 第 7 節 `FloatingPanel`、8.3「項目面板」）：置中的浮動視窗，專心看一個項目。
 * 是真的 modal：外殼用 Dialog 的 ModalFrame（role="dialog"、aria-modal、焦點鎖、背後 inert），名稱是標題列的 h2；
 * 點背景關上（只是看，沒有要確認的東西；編輯中的格子先 blur 送出再關），背後的捲動鎖住。清單在背後不能操作，這是和側邊面板的差別。
 * Esc 和側邊面板一樣登記在 lib/shortcuts.ts（編輯中的格子、開著的選單、疊在上面的對話框先處理），不是 ModalFrame 自己聽：
 * 所以由呼叫端放回焦點（ModalFrame 的 returnsFocus 關掉）：關上時要回到清單上「現在」那一筆，不是打開時那一筆。
 * 掛上時焦點到標題；標題列黏在上面，內容自己捲（region 是捲動容器，換項目時捲回最上面也是捲它）。
 * 寬度預設是畫面的 85%（不比 --dialog-width-item 窄、不比 --dialog-width-item-max 寬），左右兩緣可以拖拉調整、記在這個瀏覽器
 * （useFloatingPanelWidth、FloatingPanelResizer），兩邊照樣留著遮罩，看得到、點得到後面的清單。
 * 右下角的 AI 助理（按鈕、「×」與面板）開著時放進這個視窗裡（hostsAssistant），浮在視窗內容之上、可以操作；關掉回到 body。
 */
export function FloatingPanel({ title, headerActions, onClose, closeLabel = '關閉', headingRef, children }: FloatingPanelProps) {
  const setHeading = useHeadingFocus(headingRef);
  const headingId = useId();
  const regionRef = useRef<HTMLDivElement>(null);
  const width = useFloatingPanelWidth(regionRef);
  useShortcut(SHORTCUTS.closePanel, onClose);
  // 點遮罩時焦點留在編輯中的格子（遮罩的 mousedown 不搬焦點），格子靠 blur 送出；視窗一卸下值就丟了。先讓焦點離開再關，
  // 和側邊面板點別處、按關閉鈕一樣會先送出。
  const closeFromBackdrop = () => {
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
    onClose();
  };
  return (
    <ModalFrame
      labelledBy={headingId}
      size="item"
      onBackdropClick={closeFromBackdrop}
      returnsFocus={false}
      isBackgroundInert
      locksBackgroundScroll
      allowsShortcuts
      hostsAssistant
      variables={width.cssWidth === null ? undefined : { '--floating-panel-width': `${String(width.cssWidth)}px` }}
    >
      <div ref={regionRef} className={styles.region} data-panel-scroll="">
        <PanelHeader
          title={title}
          headerActions={headerActions}
          onClose={onClose}
          closeLabel={closeLabel}
          setHeading={setHeading}
          headingId={headingId}
        />
        <div className={panelStyles.body}>{children}</div>
      </div>
      {/* 兩緣的把手在捲動的區域外面（不跟著捲），點得到的範圍大多在視窗外面。 */}
      <FloatingPanelResizer api={width} edge="start" />
      <FloatingPanelResizer api={width} edge="end" />
    </ModalFrame>
  );
}
