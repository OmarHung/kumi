import { useLayoutEffect, useRef, type ReactNode, type RefObject } from 'react';
import { Icon } from '../../../components/Icon/Icon';
import { focusableIn } from '../../../lib/focusable';
import { watchEditorPlacement } from './editorPlacement';
import styles from './Cells.module.css';

/** 掛上時放好往右或往左長，之後位置可能變了就再放（editorPlacement.ts）。 */
function usePlacement(frameRef: RefObject<HTMLDivElement | null>): void {
  useLayoutEffect(() => {
    const frame = frameRef.current;
    return frame === null ? undefined : watchEditorPlacement(frame);
  }, [frameRef]);
}

interface EditorFrameProps {
  readonly isSaving: boolean;
  readonly problem: ReactNode;
  readonly problemId: string;
  readonly children: ReactNode;
}

/**
 * 編輯中的格子浮起來的那一層（docs/DESIGN.md 8.3「就地編輯」）：編輯器、儲存中的狀態、錯誤說明（編輯器用 aria-describedby
 * 指向它）。疊在原本的格子上面、不參與表格的版面，欄寬與列高都不變；框由這一層畫（只有一圈），有錯時換成危險色。
 * data-cell-editor（lib/rovingGrid.ts 的 CELL_EDITOR_ATTRIBUTE）：表格的方向鍵不碰裡面的按鍵與 tabIndex，
 * 輸入框的左右鍵、下拉的上下鍵、日期的方向鍵、多選的勾選框與「完成」都是編輯器自己的。
 */
export function EditorFrame({ isSaving, problem, problemId, children }: EditorFrameProps) {
  const hasProblem = problem !== null;
  const frameRef = useRef<HTMLDivElement>(null);
  usePlacement(frameRef);
  // 打開編輯器就是要打字：焦點放到第一個欄位（多選是第一個勾選框）。preventScroll：標題是固定欄，瀏覽器自己的捲動
  // 會把橫向捲過去的表格捲回最左邊；表格自己捲（lib/revealCell.ts）。
  useLayoutEffect(() => {
    const frame = frameRef.current;
    if (frame !== null) focusableIn(frame)[0]?.focus({ preventScroll: true });
  }, []);
  return (
    <div ref={frameRef} className={styles.editor} data-invalid={hasProblem || undefined} data-cell-editor="">
      {children}
      {/* 一進入編輯就掛著、只換字：和文字同時插進去的 live region 多數報讀軟體不唸。外框不標 aria-busy（會壓住裡面的
          live region），送出中由編輯器自己標（下拉、日期的 aria-busy，輸入框的唯讀）。 */}
      <span role="status" className={styles.saving}>
        {isSaving ? '儲存中…' : ''}
      </span>
      {hasProblem && (
        <div id={problemId} role="alert" className={styles.problem}>
          <Icon name="error" className={styles.problemIcon} />
          <div className={styles.problemBody}>{problem}</div>
        </div>
      )}
    </div>
  );
}
