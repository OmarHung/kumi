import { useLayoutEffect, useState, type RefObject } from 'react';
import {
  clampFloatingPanelWidth,
  floatingPanelBounds,
  measureFloatingPanel,
  readFloatingPanelWidth,
  saveFloatingPanelWidth,
  UNMEASURED,
  type FloatingPanelBounds,
  type FloatingPanelMeasure,
} from './floatingPanelWidth';

/** 視窗現在的寬度與調整的方法：FloatingPanel 用，把手（FloatingPanelResizer）調。 */
export interface FloatingPanelWidthApi extends FloatingPanelBounds {
  /** 畫面上的寬度（像素）：拉過的夾在範圍裡，沒拉過是量到的預設寬度。 */
  readonly width: number;
  /** 使用者拉過、記住的寬度；null 是用預設的寬度。 */
  readonly preferred: number | null;
  /**
   * 設在視窗上的寬度（--floating-panel-width）；null 是用 CSS 的預設寬度。記住的寬度夾過下限（被改壞的值、字級調大之後
   * 比最窄還窄），上限交給 CSS 夾在畫面之內：畫面暫時變窄時記住的值不被改小。
   */
  readonly cssWidth: number | null;
  /** 視窗現在畫出來的寬度（拖曳開始時量）。 */
  readonly current: () => number;
  /** 換成這個寬度（夾回範圍內）；isFinal 時記住（重新整理之後還在）。 */
  readonly apply: (width: number, isFinal: boolean) => void;
  /** 放回原本記住的寬度（拖曳中按 Esc），不記。 */
  readonly restore: (width: number | null) => void;
  /** 回到預設寬度，拿掉記住的寬度。 */
  readonly reset: () => void;
}

function isSame(a: FloatingPanelMeasure, b: FloatingPanelMeasure): boolean {
  return a.rendered === b.rendered && a.available === b.available && a.rem === b.rem;
}

function dialogOf(inside: RefObject<HTMLElement | null>): HTMLElement | null {
  return inside.current?.closest<HTMLElement>('[role="dialog"]') ?? null;
}

/**
 * 量視窗與遮罩：掛上時在 layout effect 裡量（第一個畫面的把手就有數值），視窗或畫面變了大小時再量；一樣的話不重新渲染。
 * 沒有 ResizeObserver 的環境（jsdom）只量一次。
 */
function useMeasured(inside: RefObject<HTMLElement | null>): FloatingPanelMeasure {
  const [measured, setMeasured] = useState(UNMEASURED);
  useLayoutEffect(() => {
    const dialog = dialogOf(inside);
    if (dialog === null) return undefined;
    const update = () => {
      const next = measureFloatingPanel(dialog);
      setMeasured((previous) => (isSame(previous, next) ? previous : next));
    };
    update();
    const observer = typeof window.ResizeObserver === 'function' ? new ResizeObserver(update) : null;
    observer?.observe(dialog);
    if (dialog.parentElement !== null) observer?.observe(dialog.parentElement);
    return () => {
      observer?.disconnect();
    };
  }, [inside]);
  return measured;
}

/**
 * 浮動視窗的寬度（docs/DESIGN.md 第 7 節 FloatingPanel）。inside 是視窗裡的任何一個元素（外框在 ModalFrame 裡）。
 * 記住的是使用者拉的寬度：畫面變窄時 CSS 把視窗夾在畫面之內，記住的值不變，變回寬的時候回到原本的寬度。
 */
export function useFloatingPanelWidth(inside: RefObject<HTMLElement | null>): FloatingPanelWidthApi {
  const [preferred, setPreferred] = useState(readFloatingPanelWidth);
  const measured = useMeasured(inside);
  const bounds = floatingPanelBounds(measured);
  return {
    ...bounds,
    width: preferred === null ? measured.rendered : clampFloatingPanelWidth(preferred, bounds),
    preferred,
    cssWidth: preferred === null ? null : Math.round(Math.max(preferred, bounds.min)),
    current: () => dialogOf(inside)?.getBoundingClientRect().width ?? measured.rendered,
    apply: (width, isFinal) => {
      const applied = clampFloatingPanelWidth(width, bounds);
      setPreferred(applied);
      if (isFinal) saveFloatingPanelWidth(applied);
    },
    restore: setPreferred,
    reset: () => {
      setPreferred(null);
      saveFloatingPanelWidth(null);
    },
  };
}
