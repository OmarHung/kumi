import { useEffect, useState } from 'react';
import { Button } from '../../components/Button/Button';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { COMMAND_NAME, PRODUCT_NAME } from '../../config/product';
import { copyToClipboard } from './TokenSecretView';
import accountStyles from './AccountPage.module.css';
import styles from './ConnectAgentSection.module.css';

const COPIED_MS = 2000;

/** MCP 伺服器的網址：和網頁同一個來源的 /mcp（每個部署各自的網址，不寫死）。 */
export function mcpUrl(): string {
  return `${window.location.origin}/mcp`;
}

/** 加在 user 範圍（所有資料夾都能用；預設的 local 範圍只在執行指令的那個資料夾有效），再登入。 */
export function claudeCodeCommand(url: string): string {
  return [`claude mcp add --scope user --transport http ${COMMAND_NAME} ${url}`, `claude mcp login ${COMMAND_NAME}`].join('\n');
}

export function codexCommand(url: string): string {
  return [`codex mcp add ${COMMAND_NAME} --url ${url}`, `codex mcp login ${COMMAND_NAME}`].join('\n');
}

type CopyResult = 'copied' | 'failed' | null;

/**
 * 一段可以複製的指令：程式碼區塊加「複製」；結果放在永遠掛著的提示裡（出現時不推動版面），過一下收掉。
 * `wrap`：放不下時折行，不在區塊裡橫向捲動。給要貼進終端機執行的長指令：執行之前整段看得到。
 * `onCopy`：複製成功時呼叫（設定精靈用它判斷這一步做了）。
 */
export function CopyBlock({ label, text, wrap = false, onCopy }: { readonly label: string; readonly text: string; readonly wrap?: boolean; readonly onCopy?: () => void }) {
  const [result, setResult] = useState<CopyResult>(null);
  useEffect(() => {
    if (result === null) return undefined;
    const timer = window.setTimeout(() => {
      setResult(null);
    }, COPIED_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [result]);
  return (
    <div className={styles.block}>
      <pre className={wrap ? `${styles.code} ${styles.wrap}` : styles.code} tabIndex={0} role="region" aria-label={label}>
        <code>{text}</code>
      </pre>
      <div className={styles.copyRow}>
        <Button
          size="sm"
          aria-label={`複製${label}`}
          onClick={() => {
            void copyToClipboard(text).then((isCopied) => {
              setResult(isCopied ? 'copied' : 'failed');
              if (isCopied) onCopy?.();
            });
          }}
        >
          複製
        </Button>
        <span className={styles.result} role="status">
          {result === 'copied' ? '已複製' : null}
          {result === 'failed' ? '無法自動複製，請選取上面的文字手動複製。' : null}
        </span>
      </div>
    </div>
  );
}

/**
 * 連接 Claude Code／Codex（internal/HANDOFF-mcp.md 第 6.2 節）：這個部署的 MCP 網址與兩個用戶端各自的設定指令。
 * 兩行指令一起貼：加進去之後接著登入，瀏覽器會開授權頁，使用者按「允許」就綁好了。上面的設定精靈會自動做這些。
 */
export function ConnectAgentSection() {
  const url = mcpUrl();
  return (
    <SettingsSection
      title="連接 Claude Code／Codex"
      description={`用的是你自己的 Claude／ChatGPT 訂閱；${PRODUCT_NAME} 只提供工具，看得到的、做得到的和你自己一樣。`}
    >
      <p className={accountStyles.note}>
        MCP 網址：<code>{url}</code>。上面的設定精靈會自動做下面的事；想自己來，照下面貼進終端機。
      </p>
      <h3 className={styles.subheading}>Claude Code</h3>
      <CopyBlock label="Claude Code 的指令" text={claudeCodeCommand(url)} wrap />
      <p className={accountStyles.note}>
        第一行加進 Claude Code（所有資料夾都能用），第二行開瀏覽器到授權頁，按「允許」。
      </p>
      <h3 className={styles.subheading}>Codex</h3>
      <CopyBlock label="Codex 的指令" text={codexCommand(url)} wrap />
      <p className={accountStyles.note}>同樣，第一行加進 Codex，第二行開瀏覽器到授權頁。</p>
    </SettingsSection>
  );
}
