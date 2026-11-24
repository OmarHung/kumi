import { useAiOptions } from '../../api/assistant';
import { useOAuthGrants } from '../../api/oauth';
import { useLocalAgents } from '../../api/personalAi';
import { useAccessTokens } from '../../api/queries';
import { agentsLine, cliLine, localAgentLine, type StatusLine } from './setupStatus';

/** 看終端機那邊做完了沒的間隔。分頁看不見時 TanStack Query 不讀（refetchIntervalInBackground 預設 false）。 */
export const SETUP_POLL_MS = 5000;

const PENDING: StatusLine = { kind: 'pending', text: '讀取中…' };
const FAILED: StatusLine = { kind: 'error', text: '暫時讀不到，稍後會再試' };

export interface SetupStatus {
  readonly cli: StatusLine;
  readonly agents: StatusLine;
  readonly localAgent: StatusLine;
}

/**
 * 設定精靈第三步的三列。只有這一步畫出來時才掛上這個 hook，所以前兩步看的時候不會每 5 秒打 API；
 * 讀不到的只影響自己那一列（有舊資料時照用舊的，不閃成錯誤）。
 */
export function useSetupStatus(): SetupStatus {
  const tokens = useAccessTokens(SETUP_POLL_MS);
  const grants = useOAuthGrants(SETUP_POLL_MS);
  const machines = useLocalAgents(SETUP_POLL_MS);
  const options = useAiOptions();

  const cli = tokens.data === undefined ? (tokens.isError ? FAILED : PENDING) : cliLine(tokens.data, tokens.dataUpdatedAt);
  const agents = grants.data === undefined ? (grants.isError ? FAILED : PENDING) : agentsLine(grants.data);
  // 組織允許與否影響「沒有電腦」時怎麼說，所以選項還沒讀到、而且還沒有電腦時先不下結論。
  const hasMachines = (machines.data?.length ?? 0) > 0;
  let localAgent: StatusLine;
  if (machines.data === undefined) localAgent = machines.isError ? FAILED : PENDING;
  else if (!hasMachines && options.data === undefined) localAgent = options.isError ? localAgentLine(machines.data, false) : PENDING;
  else localAgent = localAgentLine(machines.data, options.data?.canAddPersonal ?? false);
  return { cli, agents, localAgent };
}
