import type { KeyboardEvent } from 'react';
import { resizeKeyTarget } from '../../lib/resizeKeys';
import { useDragResize, type DragTarget } from '../../lib/useDragResize';
import type { SidePanelWidthApi } from './useSidePanelWidth';
import styles from './SidePanel.module.css';

/** 拖曳的對象：把手在面板開頭那一邊（從左到右是左緣），往開頭拉是加寬。 */
function dragTargetOf(api: SidePanelWidthApi): DragTarget {
  return {
    min: api.min,
    max: api.max ?? Number.POSITIVE_INFINITY,
    edge: 'start',
    current: () => api.width,
    userWidth: () => api.preferred ?? undefined,
    apply: api.apply,
    restore: (width) => {
      api.restore(width ?? null);
    },
  };
}

interface SidePanelResizerProps {
  readonly api: SidePanelWidthApi;
  /** 面板（aside）的 id：把手控制的是它的寬度。 */
  readonly controls: string;
}

/**
 * 面板左緣調整寬度的把手（docs/DESIGN.md 第 7 節 SidePanel）。看得到的是那條細線，點得到的範圍是 --target-min 寬。
 * 數值是面板的寬度（像素）。拖曳與 Esc 取消、只按一下不記住都在 lib/useDragResize.ts（和表格的欄寬共用）。
 * 點兩下回到預設寬度。
 */
export function SidePanelResizer({ api, controls }: SidePanelResizerProps) {
  const { isDragging, ...pointerHandlers } = useDragResize(dragTargetOf(api));
  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    // 把手在面板的開頭那一邊（從左到右是左緣）：往開頭按是加寬（lib/resizeKeys.ts）。
    const target = resizeKeyTarget(event, { width: api.width, min: api.min, max: api.max, edge: 'start' });
    if (target === null) return;
    event.preventDefault();
    if (target === 'reset') {
      api.reset();
    } else {
      api.apply(target, true);
    }
  };
  return (
    <div
      role="separator"
      aria-orientation="vertical"
      aria-label="調整面板寬度"
      aria-controls={controls}
      aria-valuenow={api.width}
      aria-valuetext={`${api.width} 像素`}
      aria-valuemin={Math.round(api.min)}
      aria-valuemax={api.max === null ? undefined : Math.round(api.max)}
      tabIndex={0}
      className={styles.resizer}
      data-dragging={isDragging || undefined}
      onKeyDown={handleKeyDown}
      onDoubleClick={api.reset}
      {...pointerHandlers}
    />
  );
}
