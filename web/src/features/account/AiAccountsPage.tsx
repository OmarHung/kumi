import { AiSetupMap } from '../ai/AiSetupMap';
import { PersonalAiSections } from './PersonalAiSection';
import styles from './AccountPage.module.css';

/** 設定中心的「我的 AI 助理」：自己的 AI 供應商連線與自己電腦上的 Claude Code／Codex（本機代理）。 */
export function AiAccountsPage() {
  return (
    <div className={styles.page}>
      <AiSetupMap page="personal" />
      <PersonalAiSections />
    </div>
  );
}
