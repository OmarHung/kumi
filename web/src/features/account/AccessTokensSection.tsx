import { useId, useRef, useState } from 'react';
import { useAccessTokens } from '../../api/queries';
import type { AccessToken } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { AccessTokenList } from './AccessTokenViews';
import { CreateAccessTokenDialog } from './CreateAccessTokenDialog';
import { RevokeAccessTokenDialog } from './RevokeAccessTokenDialog';
import accountStyles from './AccountPage.module.css';

type TokenDialog = { readonly kind: 'create' } | { readonly kind: 'revoke'; readonly token: AccessToken } | null;

const SKELETON_ROWS = 2;

function TokenContent({ onRevoke }: { readonly onRevoke: (token: AccessToken, opener: HTMLButtonElement) => void }) {
  const tokens = useAccessTokens();
  if (tokens.isPending) return <Skeleton label="正在載入存取權杖…" rows={SKELETON_ROWS} />;
  if (tokens.isLoadingError) {
    const retry = () => {
      void tokens.refetch();
    };
    return <ErrorState error={tokens.error} onRetry={retry} />;
  }
  // 「現在」用資料讀到的時刻：同一份清單用同一個，過期與相對時間才一致，畫的時候也不必讀時鐘。
  const now = tokens.dataUpdatedAt;
  return (
    <>
      <RefetchErrorNotice query={tokens} message="無法更新存取權杖的清單，畫面上可能是舊的。" />
      {tokens.data.length === 0 ? (
        <p className={accountStyles.note}>還沒有存取權杖。</p>
      ) : (
        <AccessTokenList tokens={tokens.data} now={now} onRevoke={onRevoke} />
      )}
    </>
  );
}

/**
 * 存取權杖（internal/HANDOFF-pat-cli.md 第 6 節）：給 CLI 與腳本用的個人權杖。寬窄螢幕都是清單（AccessTokenViews.tsx 寫了理由），
 * 「建立存取權杖」在清單下面（沒有權杖時是一句說明加按鈕）。撤銷要確認（不能復原），撤銷之後那一列不在了，
 * 焦點放到「建立存取權杖」。
 */
export function AccessTokensSection() {
  const descriptionId = useId();
  const createRef = useRef<HTMLButtonElement>(null);
  const [dialog, setDialog] = useState<TokenDialog>(null);
  const [revokeNote, setRevokeNote] = useState<string | null>(null);
  const close = () => {
    setDialog(null);
  };
  return (
    <SettingsSection id="tokens" title="存取權杖" description="給 CLI 與腳本用的權杖，能做的事不會超過你自己的權限。" descriptionId={descriptionId}>
      <Notice tone="info">{revokeNote}</Notice>
      <TokenContent
        onRevoke={(token) => {
          setRevokeNote(null);
          setDialog({ kind: 'revoke', token });
        }}
      />
      <div className={accountStyles.actions}>
        <Button
          ref={createRef}
          aria-describedby={descriptionId}
          onClick={() => {
            setDialog({ kind: 'create' });
          }}
        >
          建立存取權杖
        </Button>
      </div>
      {dialog?.kind === 'create' && <CreateAccessTokenDialog onClose={close} />}
      {dialog?.kind === 'revoke' && (
        <RevokeAccessTokenDialog
          token={dialog.token}
          onClose={close}
          onRevoked={(wasAlreadyGone) => {
            setRevokeNote(wasAlreadyGone ? '這把權杖已經撤銷了。' : null);
            updateThenFocus(close, [createRef]);
          }}
        />
      )}
    </SettingsSection>
  );
}
