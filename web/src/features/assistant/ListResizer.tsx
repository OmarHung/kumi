import type { KeyboardEvent } from 'react';
import { resizeKeyTarget } from '../../lib/resizeKeys';
import { useDragResize, type DragTarget } from '../../lib/useDragResize';
import type { ListWidth } from './listWidth';
import styles from './Assistant.module.css';

/** 拖曳的對象：把手在列表的結尾那一邊（從左到右是右緣），往結尾拉是加寬。 */
function dragTargetOf(api: ListWidth): DragTarget {
  return {
    min: api.min,
    max: api.max,
    edge: 'end',
    current: () => api.width,
    userWidth: () => api.preferred ?? undefined,
    apply: api.apply,
    restore: (width) => {
      api.restore(width ?? null);
    },
  };
}

interface ListResizerProps {
  readonly api: ListWidth;
  /** 對話列表（aside）的 id：把手控制的是它的寬度。 */
  readonly controls: string;
}

/**
 * 對話列表與對話之間的分隔線，拖曳調整列表的寬度（照 SidePanel 的把手：Window Splitter，
 * 拖曳與 Esc 取消在 lib/useDragResize.ts，鍵盤在 lib/resizeKeys.ts）。點兩下或 Enter 回到預設 16rem。
 */
export function ListResizer({ api, controls }: ListResizerProps) {
  const { isDragging, ...pointerHandlers } = useDragResize(dragTargetOf(api));
  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const target = resizeKeyTarget(event, { width: api.width, min: api.min, max: api.max, edge: 'end' });
    if (target === null) return;
    event.preventDefault();
    if (target === 'reset') api.reset();
    else api.apply(target, true);
  };
  return (
    <div
      role="separator"
      aria-orientation="vertical"
      aria-label="調整對話列表寬度"
      aria-controls={controls}
      aria-valuenow={api.width}
      aria-valuetext={`${String(api.width)} 像素`}
      aria-valuemin={api.min}
      aria-valuemax={api.max}
      tabIndex={0}
      className={styles.listResizer}
      data-dragging={isDragging || undefined}
      onKeyDown={handleKeyDown}
      onDoubleClick={api.reset}
      {...pointerHandlers}
    />
  );
}
