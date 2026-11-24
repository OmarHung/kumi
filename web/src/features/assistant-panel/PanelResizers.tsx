import { cx } from '../../lib/cx';
import { PANEL_ID } from './panelId';
import type { EdgeResize, PanelResize } from './usePanelResize';
import styles from './AssistantPanel.module.css';

interface EdgeProps {
  readonly edge: EdgeResize;
  readonly orientation: 'vertical' | 'horizontal';
  readonly label: string;
  readonly className: string | undefined;
}

/**
 * 面板邊上的把手：role="separator"（WAI-ARIA APG 的 Window Splitter，和 SidePanel 的把手同一套），
 * 數值是面板的寬或高（像素），停得到。
 */
function EdgeHandle({ edge, orientation, label, className }: EdgeProps) {
  return (
    <div
      role="separator"
      aria-orientation={orientation}
      aria-label={label}
      aria-controls={PANEL_ID}
      aria-valuenow={edge.value}
      aria-valuemin={edge.min}
      aria-valuemax={edge.max}
      aria-valuetext={`${String(edge.value)} 像素`}
      tabIndex={0}
      className={cx(styles.resizer, className)}
      data-dragging={edge.isDragging || undefined}
      {...edge.handlers}
    />
  );
}

/**
 * 左緣調寬、上緣調高、左上角兩個一起調（docs/design/07-components.md 的 AssistantPanel）。
 * 左上角只給指標（aria-hidden）：兩個邊的鍵盤已經涵蓋了，多一個停駐點只是多按一次 Tab。
 */
export function PanelResizers({ resize }: { readonly resize: PanelResize }) {
  const { corner } = resize;
  return (
    <>
      <EdgeHandle edge={resize.width} orientation="vertical" label="調整面板寬度" className={styles.resizerWidth} />
      <EdgeHandle edge={resize.height} orientation="horizontal" label="調整面板高度" className={styles.resizerHeight} />
      <div
        aria-hidden="true"
        className={cx(styles.resizer, styles.resizerCorner)}
        data-dragging={corner.isDragging || undefined}
        {...corner.handlers}
      />
    </>
  );
}
