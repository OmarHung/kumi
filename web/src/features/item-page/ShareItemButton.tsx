import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import type { Item } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Floating } from '../../components/Floating/Floating';
import { Icon } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { shareLink, type ShareOutcome } from '../../lib/shareLink';
import { useMediaQuery } from '../../lib/useMediaQuery';
import { itemPagePath } from '../work-items/itemLinks';
import styles from './ShareItemButton.module.css';

/** 觸控：和 tokens.css 的 @media (pointer: coarse) 同一個查詢。 */
const COARSE_QUERY = '(pointer: coarse)';

/** 複製好的打勾留多久（毫秒）：看得到、又不會一直掛著。 */
const COPIED_MS = 2000;

const DESCRIPTION = '複製這個項目的連結，貼給別人就能打開';

interface ShareItemButtonProps {
  readonly item: Pick<Item, 'key' | 'title' | 'projectKey'>;
  /** 只有圖示（項目面板的標題列）；整頁是帶字的按鈕。名稱、做的事都一樣。 */
  readonly isCompact?: boolean;
}

/** 這一次的結果：打勾的樣子、看得到的提示與唸的話跟著它，過一下回到 null。 */
function useOutcome(): [ShareOutcome | null, (outcome: ShareOutcome | null) => void] {
  const [outcome, setOutcome] = useState<ShareOutcome | null>(null);
  useEffect(() => {
    if (outcome === null) return undefined;
    const timer = window.setTimeout(() => {
      setOutcome(null);
    }, COPIED_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [outcome]);
  return [outcome, setOutcome];
}

/**
 * 看得到的提示：浮在按鈕旁邊（Floating，在 top layer，不被面板、視窗裁掉），不擋點擊。只給看的人——報讀軟體由一直掛著的
 * status／alert 唸，提示本身 aria-hidden，不重複唸。按鈕被捲出看得到的範圍時收掉（onHidden）。
 */
function CopyHint({ outcome, anchorRef, onHidden }: {
  readonly outcome: 'copied' | 'failed';
  readonly anchorRef: RefObject<HTMLElement | null>;
  readonly onHidden: () => void;
}) {
  const isCopied = outcome === 'copied';
  return (
    <Floating anchorRef={anchorRef} align="end" onAnchorHidden={onHidden} className={styles.hint}>
      <span aria-hidden="true" className={styles.hintBody} data-tone={isCopied ? 'success' : 'danger'}>
        <Icon name={isCopied ? 'check' : 'warning'} />
        {isCopied ? '已複製連結' : '無法複製連結'}
      </span>
    </Floating>
  );
}

/**
 * 項目頁與項目面板標題列的「分享」（docs/DESIGN.md 8.3「分享項目」）：按一下就把項目的連結（項目頁的完整網址）放進剪貼簿，
 * 觸控裝置打開系統的分享。名稱固定是「分享」；複製好了圖示換成打勾、旁邊浮出「已複製連結」一下（使用者 2026-11-21：
 * 只換圖示看不出來有沒有複製），報讀軟體唸「已複製 MNT-3 的連結。」；失敗時同樣浮出提示並說一聲。系統的分享自己會說，不再提示。
 * 按鈕不停用：一直按得到，再按一次就再複製一次。
 */
export function ShareItemButton({ item, isCompact = false }: ShareItemButtonProps) {
  const isCoarse = useMediaQuery(COARSE_QUERY);
  const [outcome, setOutcome] = useOutcome();
  const buttonRef = useRef<HTMLButtonElement>(null);
  const hideHint = useCallback(() => {
    setOutcome(null);
  }, [setOutcome]);
  const isCopied = outcome === 'copied';
  const shared = {
    ref: buttonRef,
    'data-copied': isCopied || undefined,
    className: styles.share,
    onClick: () => {
      const url = new URL(itemPagePath(item.projectKey, item.key), window.location.origin).href;
      void shareLink({ url, title: `${item.key} ${item.title}` }, isCoarse).then(setOutcome);
    },
  };
  const icon = isCopied ? 'check' : 'share';
  return (
    <>
      {isCompact ? (
        <IconButton icon={icon} label="分享" description={DESCRIPTION} {...shared} />
      ) : (
        <Button size="sm" variant="secondary" title={DESCRIPTION} {...shared}>
          <Icon name={icon} />
          分享
        </Button>
      )}
      {(outcome === 'copied' || outcome === 'failed') && <CopyHint outcome={outcome} anchorRef={buttonRef} onHidden={hideHint} />}
      <span role="status" className="visually-hidden">
        {isCopied ? `已複製 ${item.key} 的連結。` : ''}
      </span>
      {outcome === 'failed' && (
        <span role="alert" className="visually-hidden">
          無法複製連結，請再試一次。
        </span>
      )}
    </>
  );
}
