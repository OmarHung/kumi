import { useId } from 'react';
import { COMMAND_NAME } from '../../config/product';
import { CopyBlock } from './ConnectAgentSection';
import { ENABLE_COMMAND, START_COMMAND, loginCommand } from './localAgentText';
import accountStyles from './AccountPage.module.css';
import localStyles from './LocalAgent.module.css';
import styles from './SetupWizard.module.css';

/** 看自動啟動有沒有設定、有沒有在跑、記錄檔最後 10 行：離線時第一個要看的。 */
export const STATUS_COMMAND = `${COMMAND_NAME} agent status`;

/**
 * 設定精靈第三步：本機代理有登記、但沒有一台在線時，教怎麼在那台電腦上排查與修復（docs/CLI.md「疑難排解」）。
 * 照最常見的順序排：先用 `kumi agent status` 看原因，再依記錄檔最後寫的那一行對症處理；修好之後這一塊會自己消失。
 * 記錄檔的原因用 CLI 實際印的說法寫（權杖、沒有登入、組織沒有開放、連不上），使用者才對得起來。
 */
export function LocalAgentOfflineHelp() {
  const headingId = useId();
  const origin = window.location.origin;
  return (
    <section className={styles.help} aria-labelledby={headingId}>
      <h4 id={headingId} className={styles.helpTitle}>
        本機代理離線時
      </h4>
      <p className={accountStyles.note}>電腦要開著、沒有睡眠，並且連得上 Kumi。在那台電腦的終端機照順序檢查：</p>
      <ol className={localStyles.steps}>
        <li>
          <p>先看本機代理的狀態：有沒有設定自動啟動、現在有沒有在執行，以及記錄檔最後幾行寫了什麼。</p>
          <CopyBlock label="看狀態的指令" text={STATUS_COMMAND} />
        </li>
        <li>
          <p>「執行中：沒有」：重新在背景啟動（換過 {COMMAND_NAME} 的位置、或執行過 <code>{COMMAND_NAME} update</code> 之後也要執行一次）。</p>
          <CopyBlock label="重新在背景啟動的指令" text={ENABLE_COMMAND} />
          <p className={accountStyles.note}>
            不想在背景執行的話，改用 <code>{START_COMMAND}</code>，視窗要一直開著。
          </p>
        </li>
        <li>
          <p>
            記錄檔說權杖無效、過期、被撤銷，或「這把權杖不能當本機代理用」（本機代理的權杖 90 天到期）：重新登入，再執行一次 <code>{ENABLE_COMMAND}</code>。
          </p>
          <CopyBlock label="重新登入本機代理的指令" text={loginCommand(origin)} wrap />
        </li>
        <li>
          <p>
            記錄檔說 Claude Code 或 Codex 沒有登入、或找不到能用的引擎：在終端機執行 <code>claude</code> 依畫面登入（Codex 是{' '}
            <code>codex login</code>），再執行一次 <code>{ENABLE_COMMAND}</code>。
          </p>
        </li>
        <li>
          <p>記錄檔說組織沒有開放成員使用自己的 AI 帳號：請組織管理員到「AI 設定」打開「允許成員使用自己的 AI 帳號」。</p>
        </li>
        <li>
          <p>記錄檔一直在重試、連不上伺服器：背景的本機代理會自己重試，檢查網路、VPN 或 proxy 就好。</p>
        </li>
      </ol>
    </section>
  );
}
