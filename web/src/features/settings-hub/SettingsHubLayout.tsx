import { Suspense, useEffect, useMemo, useRef, useState } from 'react';
import { Outlet, useLocation } from 'react-router';
import { useCurrentUser } from '../../auth/CurrentUser';
import { PageBar, type Crumb } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import { scrollToPageTop } from '../../lib/pageTop';
import { useIsBelowWide } from '../../lib/useMediaQuery';
import { SettingsHubProvider } from './SettingsHubContext';
import { SettingsHubNav, SettingsHubSelect } from './SettingsHubNav';
import { findSettingsHubPage, visibleSettingsHubPages } from './settingsHubPages';
import styles from './SettingsHub.module.css';

const HUB_LABEL = '設定';
const CRUMBS: readonly Crumb[] = [{ label: HUB_LABEL }];

/**
 * 換到設定中心的另一頁時捲回最上面：外框不重新掛上，瀏覽器不會自己捲（從一頁的中段按黏住的次導覽，新的一頁會停在同一個高度）。
 * 網址帶著錨點時不捲：那一塊自己會捲過去（SettingsSection）。第一次掛上不必捲。
 */
function useScrollTopOnPageChange(pathname: string, hash: string) {
  const previous = useRef(pathname);
  useEffect(() => {
    if (previous.current === pathname) return;
    previous.current = pathname;
    if (hash === '') scrollToPageTop();
  }, [pathname, hash]);
}

/**
 * 設定中心（docs/design/08-interaction.md 8.3「設定中心」）：我的帳號、通知、AI 帳號、命令列與整合，
 * 加上組織的說明格式、寄信設定、AI 設定、主機 AI 代理，共用一個外框——頁列（位置「設定」、頁名 h1）、左邊的次導覽、右邊的內容。
 * 這是外層的版面路由：換頁時頁列與次導覽不重新掛上，按了次導覽的連結焦點留在原地（和專案設定頁的區塊分頁一樣）；
 * 各頁只畫內容（第一塊標 data-content-start），頁列的動作用 SettingsHubActions、標題的 ref 用 useSettingsHubHeading。
 */
export function SettingsHubLayout() {
  const user = useCurrentUser();
  const { pathname, hash } = useLocation();
  useScrollTopOnPageChange(pathname, hash);
  const isBelowWide = useIsBelowWide();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const [actionsHost, setActionsHost] = useState<HTMLDivElement | null>(null);
  const pages = visibleSettingsHubPages(user);
  const current = findSettingsHubPage(pathname);
  const context = useMemo(() => ({ headingRef, actionsHost }), [actionsHost]);
  const title = current?.label ?? HUB_LABEL;

  return (
    <SettingsHubProvider value={context}>
      <PageLayout
        variant={current?.variant ?? 'reading'}
        bar={
          <PageBar
            title={title}
            crumbs={CRUMBS}
            headingRef={headingRef}
            actions={<div ref={setActionsHost} className={styles.actionsHost} />}
          />
        }
        toolbar={isBelowWide ? <SettingsHubSelect pages={pages} current={current} /> : undefined}
      >
        <div className={styles.hub} data-has-nav={!isBelowWide}>
          {!isBelowWide && <SettingsHubNav pages={pages} current={current} />}
          <div className={styles.main}>
            <Suspense fallback={<LoadingState label={`正在載入${title}…`} />}>
              <Outlet />
            </Suspense>
          </div>
        </div>
      </PageLayout>
    </SettingsHubProvider>
  );
}
