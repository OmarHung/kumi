import { Button } from '../../components/Button/Button';
import { Dialog, DialogActions } from '../../components/Dialog/Dialog';
import { Notice } from '../../components/Notice/Notice';
import { COMMAND_NAME } from '../../config/product';
import { CopyBlock } from './ConnectAgentSection';
import { ENABLE_COMMAND, START_COMMAND, loginCommand } from './localAgentText';
import accountStyles from './AccountPage.module.css';
import styles from './LocalAgent.module.css';

/** 看自動啟動有沒有設定、有沒有在跑、記錄檔最後 10 行：離線時第一個要看的。 */
export const STATUS_COMMAND = `${COMMAND_NAME} agent status`;

/**
 * 照最常見的順序排：先用 `kumi agent status` 看原因，再依記錄檔最後寫的那一行對症處理（docs/CLI.md「疑難排解」）。
 * 記錄檔的原因用 CLI 實際印的說法寫（權杖、沒有登入、組織沒有開放、連不上），使用者才對得起來。
 */
function OfflineSteps() {
  const origin = window.location.origin;
  return (
    <ol className={styles.steps}>
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
  );
}

interface LocalAgentOfflineHelpProps {
  /** 打開之後代理上線了：對話框留著（使用者可能正在照著做），改成說已經在線。 */
  readonly isOnline: boolean;
  readonly onClose: () => void;
}

/**
 * 設定精靈第三步：本機代理有登記、但沒有一台在線時，按「已登記，目前離線」旁邊的「排查說明」打開。
 * 說明放在對話框裡，不直接攤在狀態清單下面：步驟有六項、三個指令區塊，攤開會把「完成的項目」撐得很長，
 * 而且代理一上線整塊消失、下面的內容跟著跳。開關由 StatusStep 管：上線時按鈕會不見，對話框不能跟著被卸下。
 */
export function LocalAgentOfflineHelp({ isOnline, onClose }: LocalAgentOfflineHelpProps) {
  return (
    <Dialog title="本機代理離線時怎麼排查" description="電腦要開著、沒有睡眠，並且連得上 Kumi。在那台電腦的終端機照順序檢查：" size="lg" onClose={onClose}>
      {/* 永遠掛著：live region 要先在畫面上，之後寫進去的字才會被唸出來。 */}
      <Notice tone="success">{isOnline ? '本機代理已經在線了，可以關上這個視窗。' : null}</Notice>
      <OfflineSteps />
      <DialogActions>
        <Button onClick={onClose}>關閉</Button>
      </DialogActions>
    </Dialog>
  );
}
