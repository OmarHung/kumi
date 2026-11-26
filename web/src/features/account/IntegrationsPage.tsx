import { AccessTokensSection } from './AccessTokensSection';
import { CliSection } from './CliSection';
import { useDownloadChoice } from './cliPlatform';
import { ConnectAgentSection } from './ConnectAgentSection';
import { OAuthGrantsSection } from './OAuthGrantsSection';
import { SetupWizard } from './SetupWizard';
import styles from './AccountPage.module.css';

/** 設定中心的「命令列與整合」：設定精靈、命令列工具、存取權杖、在 AI 工具裡用 Kumi（MCP）與已授權的 AI 代理。 */
export function IntegrationsPage() {
  // 精靈第一步與「命令列工具」共用同一個選擇，所以在這一層讀。
  const choice = useDownloadChoice();
  return (
    <div className={styles.page}>
      <SetupWizard choice={choice} />
      <CliSection choice={choice} />
      <AccessTokensSection />
      <ConnectAgentSection />
      <OAuthGrantsSection />
    </div>
  );
}
