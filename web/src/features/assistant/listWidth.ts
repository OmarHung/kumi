import { useState, type RefObject } from 'react';
import { remInPixels, useElementWidth } from '../../lib/useElementWidth';
import { readStoredWidth, saveStoredWidth } from '../../lib/storedWidth';

/**
 * 整頁 AI 助理的對話列表寬度（docs/design/07-components.md 的 AssistantPanel）：預設 16rem，最窄 12rem，最寬 32rem 且不超過
 * 工作區的一半；記在這個瀏覽器（個人的顯示偏好，像素，`assistant.listWidth`）。
 * 記住的是使用者拉的寬度：工作區變窄時畫面上夾回範圍內，記住的值不變，變回寬的時候回到原本的寬度。
 */
export const LIST_WIDTH_STORAGE_KEY = 'assistant.listWidth';
export const LIST_WIDTH_DEFAULT_REM = 16;
export const LIST_WIDTH_MIN_REM = 12;
export const LIST_WIDTH_MAX_REM = 32;

export interface ListWidth {
  /** 畫面上的寬度（像素，已經夾在範圍內）。 */
  readonly width: number;
  readonly min: number;
  readonly max: number;
  /** 使用者拉過、記住的寬度；null 是用預設。 */
  readonly preferred: number | null;
  readonly apply: (width: number, isFinal: boolean) => void;
  readonly restore: (width: number | null) => void;
  readonly reset: () => void;
}

/** 範圍（像素）。workspaceWidth 量不到（null）時只看 32rem。最窄優先：工作區很窄時也不小於最窄。 */
export function listWidthBounds(rem: number, workspaceWidth: number | null): { readonly min: number; readonly max: number } {
  const min = LIST_WIDTH_MIN_REM * rem;
  const cap = LIST_WIDTH_MAX_REM * rem;
  const half = workspaceWidth === null ? cap : Math.floor(workspaceWidth / 2);
  return { min, max: Math.max(min, Math.min(cap, half)) };
}

export function useListWidth(workspaceRef: RefObject<HTMLElement | null>): ListWidth {
  const [preferred, setPreferred] = useState(() => readStoredWidth(LIST_WIDTH_STORAGE_KEY));
  const workspaceWidth = useElementWidth(workspaceRef);
  const [rem] = useState(remInPixels);
  const { min, max } = listWidthBounds(rem, workspaceWidth);
  const clamp = (value: number) => Math.round(Math.min(Math.max(value, min), max));
  return {
    width: clamp(preferred ?? LIST_WIDTH_DEFAULT_REM * rem),
    min: Math.round(min),
    max: Math.round(max),
    preferred,
    apply: (width, isFinal) => {
      const applied = clamp(width);
      setPreferred(applied);
      if (isFinal) saveStoredWidth(LIST_WIDTH_STORAGE_KEY, applied);
    },
    restore: setPreferred,
    reset: () => {
      setPreferred(null);
      saveStoredWidth(LIST_WIDTH_STORAGE_KEY, null);
    },
  };
}
