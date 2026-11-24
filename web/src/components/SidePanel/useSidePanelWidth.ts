import { createContext, useLayoutEffect, useState, type RefObject } from 'react';
import { useMediaQuery } from '../../lib/useMediaQuery';
import {
  clampSidePanelWidth,
  measureSidePanel,
  readSidePanelWidth,
  saveSidePanelWidth,
  sidePanelBounds,
  UNMEASURED,
  type SidePanelBounds,
  type SidePanelMeasure,
} from './sidePanelWidth';

/** 觸控：和 tokens.css 的 @media (pointer: coarse) 同一個查詢。 */
const COARSE_QUERY = '(pointer: coarse)';

/** 面板現在的寬度與調整的方法：SidePanelLayout 提供，SidePanel 的把手用。 */
export interface SidePanelWidthApi extends SidePanelBounds {
  /** 畫面上的寬度（像素，已經夾在範圍內）。 */
  readonly width: number;
  /** 使用者拉過、記住的寬度；null 是用預設的寬度。可能比現在的範圍寬（畫面上夾回範圍內，記住的不變）。 */
  readonly preferred: number | null;
  /** 換成這個寬度（夾回範圍內）；isFinal 時記住（重新整理之後還在）。 */
  readonly apply: (width: number, isFinal: boolean) => void;
  /** 放回原本記住的寬度（拖曳中按 Esc），不記。 */
  readonly restore: (width: number | null) => void;
  /** 回到預設寬度，拿掉記住的寬度。 */
  readonly reset: () => void;
}

/** 沒有 SidePanelLayout（單獨用 SidePanel）時是 null：沒有清單可以讓，不出現把手。 */
export const SidePanelWidthContext = createContext<SidePanelWidthApi | null>(null);

function isSame(a: SidePanelMeasure, b: SidePanelMeasure): boolean {
  return (
    a.layoutWidth === b.layoutWidth &&
    a.gutter === b.gutter &&
    a.rem === b.rem &&
    a.defaultWidth === b.defaultWidth &&
    a.topOffset === b.topOffset &&
    a.bottomOffset === b.bottomOffset
  );
}

/**
 * 量清單那一塊：面板開著時量，變了大小（視窗變小、收起側欄）或頁面捲動（上下的偏移）時再量一次，
 * 一樣的話不重新渲染（捲到中間時偏移一直是 0）。開面板的那一次在 layout effect 裡量，第一個畫面就是夾好的寬度。
 * 沒有 ResizeObserver 的環境（jsdom）不跟著大小變。
 */
function useMeasured(layoutRef: RefObject<HTMLElement | null>, isOpen: boolean): SidePanelMeasure {
  const [measured, setMeasured] = useState(UNMEASURED);
  useLayoutEffect(() => {
    const element = layoutRef.current;
    if (!isOpen || element === null) return undefined;
    const update = () => {
      const next = measureSidePanel(element);
      setMeasured((previous) => (isSame(previous, next) ? previous : next));
    };
    update();
    window.addEventListener('scroll', update, { passive: true });
    const observer = typeof window.ResizeObserver === 'function' ? new ResizeObserver(update) : null;
    observer?.observe(element);
    return () => {
      window.removeEventListener('scroll', update);
      observer?.disconnect();
    };
  }, [layoutRef, isOpen]);
  return measured;
}

/** 面板的寬度，與 page 版面的面板最高要扣掉的高度（清單那一塊上下還沒捲走、不屬於它的部分）。 */
export interface SidePanelSizing extends SidePanelWidthApi {
  readonly blockOffset: number;
}

/**
 * 面板的寬度（docs/DESIGN.md 第 7 節 SidePanel）。記住的是使用者拉的寬度：清單那一塊變窄時畫面上夾回範圍內，
 * 記住的值不變，變回寬的時候回到原本的寬度。
 */
export function useSidePanelWidth(
  layoutRef: RefObject<HTMLElement | null>,
  isOpen: boolean,
  isBeside: boolean,
): SidePanelSizing {
  const [preferred, setPreferred] = useState(readSidePanelWidth);
  const isCoarse = useMediaQuery(COARSE_QUERY);
  const measured = useMeasured(layoutRef, isOpen);
  const bounds = sidePanelBounds(measured, { isBeside, isCoarse });
  return {
    ...bounds,
    width: clampSidePanelWidth(preferred ?? measured.defaultWidth, bounds),
    preferred,
    blockOffset: measured.topOffset + measured.bottomOffset,
    apply: (width, isFinal) => {
      const applied = clampSidePanelWidth(width, bounds);
      setPreferred(applied);
      if (isFinal) saveSidePanelWidth(applied);
    },
    restore: setPreferred,
    reset: () => {
      setPreferred(null);
      saveSidePanelWidth(null);
    },
  };
}
