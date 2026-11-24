import { useDeleteAiConnection, useDiscoverAiModels, useRefreshAiConnection, useSaveAiConnection } from '../../api/aiSettings';
import type { AiConnectionScope } from '../../api/aiTypes';
import {
  useDeletePersonalConnection,
  useDiscoverPersonalModels,
  useRefreshPersonalConnection,
  useSavePersonalConnection,
} from '../../api/personalAi';

/**
 * 連線的對話框與清單兩邊共用（AI 設定頁是組織的、我的帳號是自己的）：照 scope 選要打哪一組 API。
 * 兩組 hook 都要呼叫（hook 不能有條件），沒選到的那組不會被觸發，只是閒置的 mutation。
 */
export function useConnectionApi(scope: AiConnectionScope) {
  const organization = { save: useSaveAiConnection(), discover: useDiscoverAiModels() };
  const personal = { save: useSavePersonalConnection(), discover: useDiscoverPersonalModels() };
  return scope === 'organization' ? organization : personal;
}

export function useConnectionRowApi(scope: AiConnectionScope) {
  const organization = { refresh: useRefreshAiConnection(), remove: useDeleteAiConnection() };
  const personal = { refresh: useRefreshPersonalConnection(), remove: useDeletePersonalConnection() };
  return scope === 'organization' ? organization : personal;
}
