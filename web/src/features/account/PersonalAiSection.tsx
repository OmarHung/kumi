import type { ReactNode } from 'react';
import { useAiOptions } from '../../api/assistant';
import { useLocalAgents, usePersonalConnections } from '../../api/personalAi';
import { Notice } from '../../components/Notice/Notice';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import { ConnectionManager } from '../ai-settings/ConnectionList';
import { LocalAgentSection } from './LocalAgentSection';

const NOT_ALLOWED = '組織目前不允許使用自己的 AI 帳號。已經接的還在，但只能刪除。';
const NOT_ALLOWED_EMPTY = '組織目前不允許使用自己的 AI 帳號。要用的話請組織的擁有者或管理員在「AI 設定」開啟。';
const INTRO = '只有你能用。接你自己的供應商 API 金鑰（或用 OpenRouter 登入），按用量計費、算在你自己的帳號；專案的設定會送到你選的供應商。';
// 頁名已經是「AI 帳號」：這一塊叫「我的 API 金鑰」，和下一塊「本機代理」並列，說清楚是金鑰、不是訂閱。
const TITLE = '我的 API 金鑰';
/** 四種接法的對照連到這一塊（/account/ai#ai-keys）。 */
const SECTION_ID = 'ai-keys';

/**
 * AI 帳號頁的內容（internal/HANDOFF-ai-floating.md 第 5.2 節）：自己的 AI 供應商連線，浮動的 AI 助理選模型時會列出來；
 * 接著是「本機代理」（自己電腦上的 Claude Code／Codex，LocalAgentSection，另一塊）。
 * 這是獨立的一頁，不能空白：組織不允許、自己也沒有連線與電腦時，只有一塊說明。
 * 三份資料都讀到了才畫內容（載入中是形狀接近的骨架）：先畫一半再長出來會把下面的區塊往下推。
 */
/**
 * 第一塊（也是這一頁的第一筆內容）。是產生元素的函式、不是元件：載入中、讀不到與讀到之後都是同一種元素在同一個位置，
 * 資料到了 React 沿用同一個 region，不會整塊卸下再掛上（焦點與報讀軟體的位置不會掉）。
 */
function firstSection(children: ReactNode, description?: ReactNode) {
  return (
    <SettingsSection title={TITLE} description={description} id={SECTION_ID}>
      {children}
    </SettingsSection>
  );
}

export function PersonalAiSections() {
  const options = useAiOptions();
  const connections = usePersonalConnections();
  const machines = useLocalAgents();
  // 電腦的清單只在「第一次還沒有結果」時等：失敗之後重試（status 會暫時變回 pending）不能讓整段消失。
  const isFirstMachineLoad = machines.isPending && machines.errorUpdateCount === 0;
  if (connections.isPending || options.isPending || isFirstMachineLoad) {
    return firstSection(<Skeleton label="正在載入 AI 帳號…" rows={3} />);
  }
  if (connections.isError) {
    return firstSection(<ErrorState error={connections.error} onRetry={() => void connections.refetch()} />);
  }
  // 讀不到組織允不允許：不能說「組織不允許」（可能不是事實，會讓人去找管理員），說讀不到、可以重試。
  if (options.data === undefined) {
    return firstSection(<ErrorState error={options.error} title="讀不到組織的 AI 設定" onRetry={() => void options.refetch()} />);
  }
  const isAllowed = options.data.canAddPersonal;
  // 電腦的清單讀不到時當作沒有登記過（那一段自己會顯示錯誤）。
  const hasMachines = (machines.data?.length ?? 0) > 0;
  if (!isAllowed && connections.data.length === 0 && !hasMachines) {
    return firstSection(<Notice tone="info">{NOT_ALLOWED_EMPTY}</Notice>);
  }
  return (
    <>
      {firstSection(
        <ConnectionManager
          scope="personal"
          connections={connections.data}
          emptyText="還沒有接任何 API 金鑰。"
          openParam="aiConnection"
          isDeleteOnly={!isAllowed}
          addLabel="新增 API 金鑰"
        />,
        isAllowed ? INTRO : NOT_ALLOWED,
      )}
      {(isAllowed || hasMachines) && <LocalAgentSection isAllowed={isAllowed} />}
    </>
  );
}
