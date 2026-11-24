import { createContext, useContext, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';

interface SettingsHubContextValue {
  /** 頁列的 h1（tabIndex=-1）：換頁、刪除之後要把焦點放到標題的頁面用它。 */
  readonly headingRef: RefObject<HTMLHeadingElement | null>;
  /** 頁列右邊放動作的地方；還沒掛上時是 null。 */
  readonly actionsHost: HTMLElement | null;
}

const SettingsHubContext = createContext<SettingsHubContextValue | null>(null);

export const SettingsHubProvider = SettingsHubContext.Provider;

function useSettingsHub(): SettingsHubContextValue {
  const value = useContext(SettingsHubContext);
  if (value === null) throw new Error('設定中心的頁面要放在 SettingsHubLayout 裡');
  return value;
}

/** 頁列的 h1：設定中心的頁面要把焦點放到標題時用（docs/design/08-interaction.md 8.3「焦點」）。 */
export function useSettingsHubHeading(): RefObject<HTMLHeadingElement | null> {
  return useSettingsHub().headingRef;
}

/**
 * 這一頁的動作（頁列右邊，最多兩顆，docs/DESIGN.md 6.1）。頁列屬於設定中心的版面（換頁時不重新掛上，
 * 次導覽上的焦點才不會掉），所以頁面用 portal 把自己的按鈕放進去；離開這一頁時跟著卸下。
 */
export function SettingsHubActions({ children }: { readonly children: ReactNode }) {
  const host = useSettingsHub().actionsHost;
  return host === null ? null : createPortal(children, host);
}
