import { useLocation, useMatch } from 'react-router';
import { useIsAssistantPanelShown } from '../assistant-panel/panelControl';
import { useHasAssistantEntry } from '../assistant-panel/useAssistantEntry';

/**
 * 底部分頁列與圖示列共用的「目前所在」判斷：哪一格亮著、AI 助理是整頁還是浮動面板。
 * 浮動面板開著時它是目前所在的那一格，其他格都不標。
 */
export function useNavState() {
  const { pathname, search } = useLocation();
  const hasAssistant = useHasAssistantEntry();
  const isPanelShown = useIsAssistantPanelShown();
  const isOrganizationAssistant = useMatch({ path: '/assistant', end: false }) !== null;
  const isProjectAssistant = useMatch({ path: '/projects/:projectKey/assistant', end: false }) !== null;
  const isAssistantPage = isOrganizationAssistant || isProjectAssistant;
  const isUnder = (prefix: string) => pathname === prefix || pathname.startsWith(`${prefix}/`);
  const isProjects = isUnder('/projects') && !isAssistantPage;
  return {
    pathname,
    search,
    hasAssistant,
    isPanelShown,
    isAssistantPage,
    isFullPageAssistant: isAssistantPage && !isPanelShown,
    isProjects,
    isUnder,
    // 在專案的頁面底下時，「專案」就是這一頁：連回目前的網址，不把人帶離專案；其他頁才到專案清單。
    projectsTarget: isProjects ? `${pathname}${search}` : '/projects',
    noneActive: isPanelShown,
  };
}
