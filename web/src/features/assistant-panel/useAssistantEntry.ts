import { useAiOptions } from '../../api/assistant';

/** 這個人有沒有 AI 助理可以用：組織有開放的模型，或允許自己接 AI 帳號。選項還沒讀到是 false。 */
export function useHasAssistantEntry(): boolean {
  const options = useAiOptions();
  return options.data?.available === true || options.data?.canAddPersonal === true;
}
