import type { AiEffort, AiOptionKind, AiProviderKind } from '../../api/aiTypes';

/** 能存成連線的供應商在畫面上的名稱（AiProviderKind；本機的引擎不能存成連線，見 OPTION_LABELS）。 */
export const PROVIDER_LABELS: Readonly<Record<AiProviderKind, string>> = {
  anthropic: 'Anthropic',
  openai: 'OpenAI',
  google: 'Google Gemini',
  openai_compatible: 'OpenAI 相容',
  openrouter: 'OpenRouter',
};

/** 能選的種類（AiOptionKind）：供應商加上自己電腦上的 Claude Code、Codex。 */
export const OPTION_LABELS: Readonly<Record<AiOptionKind, string>> = {
  ...PROVIDER_LABELS,
  claude_code: 'Claude Code',
  codex: 'Codex',
};

/** 思考深度在畫面上的說法，由淺到深。null（不指定）是「預設」。 */
export const EFFORT_LABELS: Readonly<Record<AiEffort, string>> = {
  none: '不思考',
  low: '低',
  medium: '中',
  high: '高',
  xhigh: '很高',
  max: '最高',
};

export const DEFAULT_EFFORT_LABEL = '預設';

export function effortLabel(effort: AiEffort | null): string {
  return effort === null ? DEFAULT_EFFORT_LABEL : EFFORT_LABELS[effort];
}

/** AI 助理不屬於任何專案的對話（組織層級）在畫面上的說法：面板的專案選單、主機用量。 */
export const NO_PROJECT_LABEL = '不選專案';

/** 助理用了哪個工具，畫面上的說法；進行中的在後面接「…中」由畫面決定。不認得的名稱照原文。 */
const TOOL_LABELS: Readonly<Record<string, string>> = {
  get_blueprint: '讀取目前的設定',
  list_templates: '查看模板',
  get_template: '讀取模板',
  describe_usage: '查看使用狀況',
  preview_changeset: '預覽修改',
  propose_changeset: '提出變更集',
  get_item: '讀取項目',
  search_items: '搜尋項目',
  propose_item_changes: '建議修改',
  list_projects: '查看你的專案清單',
  propose_project: '提出新專案',
};

/** itemKey：工具的段落帶得到編號時給（get_item 寫「讀取 WEB-12」）；沒有就寫「讀取項目」。 */
export function toolLabel(name: string, itemKey?: string | null): string {
  if (name === 'get_item' && itemKey !== undefined && itemKey !== null) return `讀取 ${itemKey}`;
  return TOOL_LABELS[name] ?? name;
}

/** 到哪裡拿 API 金鑰（新分頁開）。相容端點沒有固定的地方。 */
export const API_KEY_PAGES: Readonly<Partial<Record<AiProviderKind, string>>> = {
  anthropic: 'https://platform.claude.com/settings/keys',
  openai: 'https://platform.openai.com/api-keys',
  google: 'https://aistudio.google.com/apikey',
  openrouter: 'https://openrouter.ai/settings/keys',
};
