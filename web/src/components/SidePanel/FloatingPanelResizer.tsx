import type { KeyboardEvent } from 'react';
import { resizeKeyTarget } from '../../lib/resizeKeys';
import { useDragResize, type DragTarget } from '../../lib/useDragResize';
import type { FloatingPanelWidthApi } from './useFloatingPanelWidth';
import styles from './FloatingPanel.module.css';

type Edge = 'start' | 'end';

/** 拖曳的對象：視窗置中，兩邊一起動（指標移 1px 寬度變 2px）；往把手那一邊拉是加寬。 */
function dragTargetOf(api: FloatingPanelWidthApi, edge: Edge): DragTarget {
  return {
    min: api.min,
    max: api.max ?? Number.POSITIVE_INFINITY,
    edge,
    isCentered: true,
    current: api.current,
    userWidth: () => api.preferred ?? undefined,
    apply: api.apply,
    restore: (width) => {
      api.restore(width ?? null);
    },
  };
}

function handleKeyDown(event: KeyboardEvent<HTMLDivElement>, api: FloatingPanelWidthApi): void {
  const target = resizeKeyTarget(event, { width: api.width, min: api.min, max: api.max, edge: 'end' });
  if (target === null) return;
  event.preventDefault();
  if (target === 'reset') api.reset();
  else api.apply(target, true);
}

interface FloatingPanelResizerProps {
  readonly api: FloatingPanelWidthApi;
  readonly edge: Edge;
}

/**
 * 浮動視窗左右兩緣調整寬度的把手（docs/DESIGN.md 第 7 節 FloatingPanel）：拖曳、點兩下回到預設寬度。
 * 點得到的範圍大多在視窗外面（遮罩上），只伸進視窗一點點：不蓋住右緣的捲軸與內容。
 * 鍵盤只用右緣那一個（role="separator"，Tab 走得到，按鍵和側邊面板的把手一樣，lib/resizeKeys.ts）；
 * 左緣那一個只給指標，對報讀軟體隱藏、不是停駐點：兩個把手調的是同一個寬度，停駐點多一個只是多按一次 Tab。
 */
export function FloatingPanelResizer({ api, edge }: FloatingPanelResizerProps) {
  const { isDragging, ...pointerHandlers } = useDragResize(dragTargetOf(api, edge));
  const isKeyboard = edge === 'end';
  return (
    <div
      role="separator"
      aria-hidden={isKeyboard ? undefined : true}
      aria-orientation="vertical"
      aria-label="調整視窗寬度"
      aria-valuenow={api.width}
      aria-valuetext={`${String(api.width)} 像素`}
      aria-valuemin={Math.round(api.min)}
      aria-valuemax={api.max === null ? undefined : Math.round(api.max)}
      tabIndex={isKeyboard ? 0 : undefined}
      className={styles.resizer}
      data-edge={edge}
      data-dragging={isDragging || undefined}
      onKeyDown={
        isKeyboard
          ? (event) => {
              handleKeyDown(event, api);
            }
          : undefined
      }
      onDoubleClick={api.reset}
      {...pointerHandlers}
    />
  );
}
