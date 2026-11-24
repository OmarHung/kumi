import { useSyncExternalStore } from 'react';

/**
 * 右下角的 AI 助理按鈕被使用者拖到哪裡：「離畫面右邊、下邊多遠」（像素），個人的顯示偏好，記在這個瀏覽器（登出不清）。
 * 沒記過（null）就是預設的右下角，由 CSS 決定。按鈕、面板（要對齊按鈕）與叫回來的帳號選單不在同一棵樹裡，所以做成小小的外部狀態，
 * 和 launcherPreference 一樣：讀寫被擋下（私密瀏覽）時讀不到當成沒記過；寫不進去時這次照樣依使用者拖的，記在 override，下次載入回到預設。
 * 記住的值一律不因畫面變小而改：畫面上夾回去由 CSS 做。
 */
export const LAUNCHER_POSITION_STORAGE_KEY = 'assistant.launcherPosition';

export interface LauncherPosition {
  readonly right: number;
  readonly bottom: number;
}

/** 預設的右下角（頁面左右留白、--space-4）：「接近預設」的判斷用，實際的預設由 CSS 畫。 */
export const DEFAULT_LAUNCHER_POSITION: LauncherPosition = { right: 24, bottom: 16 };
/** 按鈕離這個範圍以內都算「還在右下角」：新通知的提示照舊讓位。 */
export const NEAR_DEFAULT_DISTANCE = 48;
/** 按鈕離畫面四邊至少留的距離（--space-2）。 */
export const LAUNCHER_EDGE_MARGIN = 8;
/** 觸控時「×」疊在按鈕正上方（44px 加間距），上緣要多留這一塊才不會跑出畫面。 */
export const COARSE_TOP_MARGIN = 60;
export const LAUNCHER_FALLBACK_SIZE = 48;

const listeners = new Set<() => void>();
let override: { readonly value: LauncherPosition | null } | null = null;
let cache: { readonly raw: string | null; readonly value: LauncherPosition | null } = { raw: null, value: null };

function isCoordinate(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0;
}

function parse(raw: string | null): LauncherPosition | null {
  if (raw === null) return null;
  try {
    const value = JSON.parse(raw) as Partial<LauncherPosition> | null;
    if (value === null || typeof value !== 'object' || !isCoordinate(value.right) || !isCoordinate(value.bottom)) return null;
    return { right: value.right, bottom: value.bottom };
  } catch {
    return null;
  }
}

/** 同一段文字回同一個物件：useSyncExternalStore 要求沒變就是同一個參照。 */
export function readLauncherPosition(): LauncherPosition | null {
  if (override !== null) return override.value;
  let raw: string | null;
  try {
    raw = localStorage.getItem(LAUNCHER_POSITION_STORAGE_KEY);
  } catch {
    return null;
  }
  if (raw !== cache.raw) cache = { raw, value: parse(raw) };
  return cache.value;
}

/** null 是放回預設的右下角。 */
export function setLauncherPosition(position: LauncherPosition | null): void {
  try {
    if (position === null) localStorage.removeItem(LAUNCHER_POSITION_STORAGE_KEY);
    else localStorage.setItem(LAUNCHER_POSITION_STORAGE_KEY, JSON.stringify(position));
    override = null;
  } catch {
    override = { value: position };
  }
  listeners.forEach((listener) => {
    listener();
  });
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  const onStorage = (event: StorageEvent) => {
    if (event.key === LAUNCHER_POSITION_STORAGE_KEY || event.key === null) listener();
  };
  window.addEventListener('storage', onStorage);
  return () => {
    listeners.delete(listener);
    window.removeEventListener('storage', onStorage);
  };
}

export function useLauncherPosition(): LauncherPosition | null {
  return useSyncExternalStore(subscribe, readLauncherPosition, () => null);
}

/** 沒移動過、或離預設的右下角 48px 以內：新通知的提示要讓位。 */
export function isNearDefaultPosition(position: LauncherPosition | null): boolean {
  if (position === null) return true;
  return (
    Math.abs(position.right - DEFAULT_LAUNCHER_POSITION.right) <= NEAR_DEFAULT_DISTANCE &&
    Math.abs(position.bottom - DEFAULT_LAUNCHER_POSITION.bottom) <= NEAR_DEFAULT_DISTANCE
  );
}

export interface LauncherBounds {
  readonly viewport: { readonly width: number; readonly height: number };
  readonly size: number;
  readonly isCoarse: boolean;
}

/** 拖曳中夾在畫面裡（四邊至少留邊距；觸控時上緣多留「×」的位置）。取整數。和 CSS 的夾法是同一套。 */
export function clampLauncherPosition(position: LauncherPosition, { viewport, size, isCoarse }: LauncherBounds): LauncherPosition {
  const maxRight = Math.max(LAUNCHER_EDGE_MARGIN, viewport.width - size - LAUNCHER_EDGE_MARGIN);
  const maxBottom = Math.max(
    LAUNCHER_EDGE_MARGIN,
    viewport.height - size - (isCoarse ? COARSE_TOP_MARGIN : LAUNCHER_EDGE_MARGIN),
  );
  return {
    right: Math.round(Math.min(Math.max(position.right, LAUNCHER_EDGE_MARGIN), maxRight)),
    bottom: Math.round(Math.min(Math.max(position.bottom, LAUNCHER_EDGE_MARGIN), maxBottom)),
  };
}
