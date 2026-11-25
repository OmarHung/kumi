import type { AccessToken } from '../../api/types';
import { Badge } from '../../components/Badge/Badge';
import { Button } from '../../components/Button/Button';
import { List, ListRow } from '../../components/List/List';
import { COMMAND_NAME } from '../../config/product';
import { dayText, expiryText, isExpired, lastUsedText, prefixText, revokeLabel, scopesText } from './accessTokenText';
import styles from './AccessTokens.module.css';

interface AccessTokenViewProps {
  readonly tokens: readonly AccessToken[];
  /** 畫面用的「現在」：同一份清單用同一個，過期與相對時間才一致。 */
  readonly now: number;
  readonly onRevoke: (token: AccessToken, opener: HTMLButtonElement) => void;
}

function RevokeButton({ token, onRevoke }: { readonly token: AccessToken; readonly onRevoke: AccessTokenViewProps['onRevoke'] }) {
  return (
    <Button
      variant="ghost"
      size="sm"
      aria-label={revokeLabel(token)}
      onClick={(event) => {
        onRevoke(token, event.currentTarget);
      }}
    >
      撤銷
    </Button>
  );
}

/** 到期：「不過期」或「日期 到期」；過期了在後面標「已過期」（不只靠顏色）。 */
function Expiry({ token, now }: { readonly token: AccessToken; readonly now: number }) {
  return (
    <>
      {token.expiresAt === null ? expiryText(token) : `${expiryText(token)} 到期`}
      {isExpired(token, now) && (
        <>
          {' '}
          <Badge tone="danger">已過期</Badge>
        </>
      )}
    </>
  );
}

/** 最後使用：「還沒用過」或「5 小時前用過」（建立與到期也是日期，畫面上要看得出哪個是哪個）。 */
function lastUsed(token: AccessToken, now: number): string {
  return token.lastUsedAt === null ? lastUsedText(token, now) : `${lastUsedText(token, now)}用過`;
}

/**
 * 存取權杖的清單（docs/DESIGN.md 7.1 的 List）：第一行名稱，第二行開頭、範圍、建立、最後使用與到期；「撤銷」在最後面。
 * 寬窄螢幕都用清單：設定中心的右欄最寬約 43rem，六欄的表格放不下（2026-11-24 實測 1305 寬時「到期」被切掉、「撤銷」整欄看不到，
 * 範圍被擠成四行）；權杖通常只有幾把，清單一眼看得完。
 */
export function AccessTokenList({ tokens, now, onRevoke }: AccessTokenViewProps) {
  return (
    <List label="存取權杖">
      {tokens.map((token) => (
        <ListRow
          key={token.tokenId}
          title={token.name}
          meta={[
            { key: 'prefix', label: '開頭', content: <span className={styles.prefix}>{prefixText(token)}</span> },
            // 瀏覽器登入建立的（machineName 有值）說明是哪台電腦：清單裡分得出哪一把是哪台電腦的、該撤銷哪一把。
            ...(token.machineName === null ? [] : [{ key: 'machine', content: `從 ${token.machineName} 的 ${COMMAND_NAME} 登入` }]),
            // 第二行有兩個日期與一個相對時間：說法直接寫在畫面上（不只給報讀軟體），才分得出建立、使用與到期。
            { key: 'scopes', content: `範圍：${scopesText(token.scopes)}` },
            { key: 'created', content: `${dayText(token.createdAt)} 建立` },
            // 建立它的電腦或瀏覽器的位址：不是自己建的時候看得出來（舊的權杖沒記，不顯示）。
            ...(token.createdFrom === null ? [] : [{ key: 'from', content: `從 ${token.createdFrom} 建立` }]),
            { key: 'used', content: lastUsed(token, now) },
            { key: 'expiry', content: <Expiry token={token} now={now} /> },
          ]}
          trailing={<RevokeButton token={token} onRevoke={onRevoke} />}
        />
      ))}
    </List>
  );
}
