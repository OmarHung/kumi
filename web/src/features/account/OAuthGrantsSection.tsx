import { useEffect, useId, useRef, useState } from 'react';
import { errorMessage } from '../../api/http';
import { useOAuthGrants, useRevokeOAuthGrant, type OAuthGrant } from '../../api/oauth';
import { Button } from '../../components/Button/Button';
import { ConfirmDialog } from '../../components/Dialog/ConfirmDialog';
import { List, ListRow } from '../../components/List/List';
import { Notice } from '../../components/Notice/Notice';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { focusableHeading } from './focusHeading';
import { dayText } from './accessTokenText';
import { consentScopeLabel, orderedScopes } from '../oauth/oauthText';
import { formatRelativeTime } from '../../domain/time';
import accountStyles from './AccountPage.module.css';

const SKELETON_ROWS = 2;

function GrantRow({ grant, now, onRevoke }: { readonly grant: OAuthGrant; readonly now: number; readonly onRevoke: (grant: OAuthGrant) => void }) {
  return (
    <ListRow
      title={grant.clientName}
      meta={[
        { key: 'host', label: '提供者', content: grant.clientHost ?? '未驗證' },
        { key: 'scopes', label: '範圍', content: orderedScopes(grant.scopes).map(consentScopeLabel).join('、') },
        { key: 'created', content: `授權於 ${dayText(grant.createdAt)}` },
        { key: 'used', content: `最後使用 ${grant.lastUsedAt === null ? '還沒用過' : formatRelativeTime(grant.lastUsedAt, now)}` },
      ]}
      trailing={
        <Button
          variant="ghost"
          size="sm"
          aria-label={`撤銷「${grant.clientName}」的授權`}
          data-revoke-grant=""
          onClick={() => {
            onRevoke(grant);
          }}
        >
          撤銷
        </Button>
      }
    />
  );
}

function GrantContent({ onRevoke }: { readonly onRevoke: (grant: OAuthGrant) => void }) {
  const grants = useOAuthGrants();
  if (grants.isPending) return <Skeleton label="正在載入已授權的 AI 代理…" rows={SKELETON_ROWS} />;
  if (grants.isLoadingError) {
    return (
      <ErrorState
        error={grants.error}
        onRetry={() => {
          void grants.refetch();
        }}
      />
    );
  }
  return (
    <>
      <RefetchErrorNotice query={grants} message="無法更新已授權的 AI 代理，畫面上可能是舊的。" />
      {grants.data.length === 0 ? (
        <p className={accountStyles.note}>還沒有授權過任何 AI 代理。</p>
      ) : (
        <List label="已授權的 AI 代理">
          {grants.data.map((grant) => (
            <GrantRow key={grant.grantId} grant={grant} now={grants.dataUpdatedAt} onRevoke={onRevoke} />
          ))}
        </List>
      )}
    </>
  );
}

function RevokeGrantDialog({ grant, onClose, onRevoked }: {
  readonly grant: OAuthGrant;
  readonly onClose: () => void;
  readonly onRevoked: (wasAlreadyGone: boolean) => void;
}) {
  const revoke = useRevokeOAuthGrant();
  return (
    <ConfirmDialog
      title={`撤銷「${grant.clientName}」的授權？`}
      description="它之後要重新登入授權才能再連。"
      confirmLabel="撤銷授權"
      pendingLabel="撤銷中…"
      tone="danger"
      isPending={revoke.isPending}
      keepsFocusWhilePending
      error={revoke.isError ? errorMessage(revoke.error) : null}
      onConfirm={() => {
        revoke.mutate(grant.grantId, {
          onSuccess: (result) => {
            onRevoked(result.wasAlreadyGone);
          },
        });
      }}
      onCancel={onClose}
    />
  );
}

/**
 * 已授權的 AI 代理（internal/HANDOFF-mcp.md 第 6.2 節）：用 OAuth 連上 pm-tool 的 Claude Code、Codex 等。
 * 每一列有「撤銷」，要確認；撤銷之後那一列不在了，清單由重抓決定。
 * 撤銷後按的那顆按鈕不在了，對話框關上時焦點無處可回：移到原位置的那一列（最後一列被撤銷就移到上一列）的「撤銷」，
 * 清單空了就移到區塊的標題。
 */
export function OAuthGrantsSection() {
  const headingId = useId();
  const grants = useOAuthGrants();
  const [revoking, setRevoking] = useState<OAuthGrant | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const pendingFocusIndex = useRef<number | null>(null);

  useEffect(() => {
    const focusIndex = pendingFocusIndex.current;
    if (focusIndex === null || revoking !== null) return;
    pendingFocusIndex.current = null;
    const heading = focusableHeading(headingId);
    const buttons = heading?.closest('section')?.querySelectorAll<HTMLElement>('[data-revoke-grant]') ?? [];
    const target = buttons[Math.min(focusIndex, buttons.length - 1)] ?? heading;
    target?.focus();
  }, [revoking, headingId]);

  return (
    <SettingsSection
      title="已授權的 AI 代理"
      headingId={headingId}
      description="你在 Claude Code、Codex 等工具裡授權連上來的（上面那一塊設定的）；它們能做的事不會超過你自己的權限。"
    >
      <Notice tone="info">{note}</Notice>
      <GrantContent
        onRevoke={(grant) => {
          setNote(null);
          setRevoking(grant);
        }}
      />
      {revoking !== null && (
        <RevokeGrantDialog
          grant={revoking}
          onClose={() => {
            setRevoking(null);
          }}
          onRevoked={(wasAlreadyGone) => {
            setNote(wasAlreadyGone ? '這筆授權已經撤銷了。' : null);
            pendingFocusIndex.current = Math.max(0, grants.data?.findIndex((entry) => entry.grantId === revoking.grantId) ?? 0);
            setRevoking(null);
          }}
        />
      )}
    </SettingsSection>
  );
}
