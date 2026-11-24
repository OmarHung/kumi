import type { ProjectBlueprint } from '../api/blueprint';

/**
 * 和真的後端回的形狀一樣（2026-11-13 用 GET /v1/projects/{key} 核對過）：
 * 選擇性的陣列會寫成空陣列（transitions 的 allowedRoles、requireFields，itemTypes 的 fields、parents），
 * 簡單任務的流程沒有列轉換時連 transitions 這個屬性都沒有。
 */

/** 模板 simple_tasks：一種類型、沒有列轉換、每種欄位型別各一個。 */
export const simpleTasksBlueprint: ProjectBlueprint = {
  apiVersion: 'blueprint/v0',
  kind: 'ProjectBlueprint',
  metadata: {
    key: 'smk',
    name: '冒煙測試',
    description: '待辦、進行中、完成三個狀態的任務看板，適合小團隊追蹤日常工作。',
    basedOn: { template: 'simple_tasks', version: '1.0.0' },
  },
  fields: [
    {
      key: 'category',
      name: '分類',
      type: 'single_select',
      options: [
        { key: 'feature', name: '新功能', color: 'blue' },
        { key: 'bug', name: '錯誤', color: 'red' },
        { key: 'chore', name: '雜務', color: 'gray' },
      ],
    },
    {
      key: 'tags',
      name: '標籤',
      type: 'multi_select',
      options: [
        { key: 'frontend', name: '前端', color: 'purple' },
        { key: 'backend', name: '後端', color: 'green' },
        { key: 'docs', name: '文件', color: 'yellow' },
      ],
    },
    { key: 'review_date', name: '檢查日', type: 'date' },
    { key: 'story_points', name: '點數', type: 'number' },
    { key: 'budget', name: '預算', type: 'currency', currency: 'TWD' },
    { key: 'blocked', name: '卡住了', type: 'checkbox' },
    { key: 'reference_url', name: '參考連結', type: 'url' },
    { key: 'reviewer', name: '審查者', type: 'person' },
    { key: 'summary', name: '摘要', type: 'text' },
    { key: 'notes', name: '備註', type: 'long_text' },
  ],
  workflows: [
    {
      key: 'task_flow',
      name: '任務流程',
      states: [
        { key: 'todo', name: '待辦', category: 'not_started' },
        { key: 'doing', name: '進行中', category: 'in_progress' },
        { key: 'done', name: '完成', category: 'done' },
      ],
    },
  ],
  itemTypes: [
    {
      key: 'task',
      name: '任務',
      workflow: 'task_flow',
      fields: [
        'category',
        'tags',
        'review_date',
        'story_points',
        'budget',
        'blocked',
        'reference_url',
        'reviewer',
        'summary',
        'notes',
      ],
      parents: [],
    },
  ],
  views: [],
  automations: [],
  roles: [],
  bindings: [],
};

function transition(
  from: string,
  to: string,
  allowedRoles: readonly string[] = [],
  requireFields: readonly string[] = [],
) {
  return { from, to, allowedRoles, requireFields };
}

/** 模板 agency_project：三種類型、任務有上層項目的限制、轉換有允許的角色與要先填的欄位。 */
export const agencyBlueprint: ProjectBlueprint = {
  apiVersion: 'blueprint/v0',
  kind: 'ProjectBlueprint',
  metadata: {
    key: 'agy',
    name: '接案測試',
    basedOn: { template: 'agency_project', version: '1.0.0' },
  },
  fields: [
    { key: 'quote_amount', name: '報價金額', type: 'currency', currency: 'TWD' },
    { key: 'client_signoff_date', name: '客戶驗收日', type: 'date' },
    { key: 'change_reason', name: '變更原因', type: 'long_text' },
    {
      key: 'impact',
      name: '影響程度',
      type: 'single_select',
      options: [
        { key: 'low', name: '低', color: 'gray' },
        { key: 'medium', name: '中', color: 'yellow' },
        { key: 'high', name: '高', color: 'red' },
      ],
    },
  ],
  workflows: [
    {
      key: 'delivery',
      name: '交付流程',
      states: [
        { key: 'confirming', name: '需求確認', category: 'not_started' },
        { key: 'quoting', name: '報價', category: 'not_started' },
        { key: 'designing', name: '設計', category: 'in_progress' },
        { key: 'developing', name: '開發', category: 'in_progress' },
        { key: 'internal_qa', name: '內部驗收', category: 'in_progress' },
        { key: 'client_qa', name: '客戶驗收', category: 'in_progress' },
        { key: 'closed', name: '結案', category: 'done' },
      ],
      transitions: [
        transition('confirming', 'quoting'),
        transition('quoting', 'designing'),
        transition('designing', 'developing'),
        transition('developing', 'internal_qa'),
        transition('internal_qa', 'developing'),
        transition('internal_qa', 'client_qa', ['project_manager']),
        transition('client_qa', 'developing'),
        transition('client_qa', 'closed', ['project_manager'], ['client_signoff_date']),
      ],
    },
    {
      key: 'task_flow',
      name: '任務流程',
      states: [
        { key: 'todo', name: '待辦', category: 'not_started' },
        { key: 'doing', name: '進行中', category: 'in_progress' },
        { key: 'done', name: '完成', category: 'done' },
      ],
    },
  ],
  itemTypes: [
    {
      key: 'requirement',
      name: '需求',
      workflow: 'delivery',
      fields: ['quote_amount', 'client_signoff_date'],
      parents: [],
    },
    {
      key: 'change_request',
      name: '需求變更單',
      workflow: 'delivery',
      fields: ['change_reason', 'impact', 'quote_amount', 'client_signoff_date'],
      parents: [],
    },
    {
      key: 'task',
      name: '任務',
      workflow: 'task_flow',
      fields: [],
      parents: ['requirement', 'change_request'],
    },
  ],
  roles: [
    {
      key: 'project_manager',
      name: '專案經理',
      permissions: [
        'item.read',
        'item.create',
        'item.update',
        'item.transition',
        'comment.create',
        'blueprint.read',
        'blueprint.propose',
        'blueprint.apply',
      ],
    },
    { key: 'client', name: '客戶', permissions: ['item.read', 'comment.create'] },
  ],
};

/**
 * 和本機 AGY 一樣的檢視、自動化與整合（2026-11-16 用 GET /v1/projects/AGY 核對過）：
 * 檢視沒有 visibleTo 時省略這個屬性；「給客戶看」只有客戶與專案經理看得到；自動化把「開發」轉到「內部驗收」。
 */
export const agencyFullBlueprint: ProjectBlueprint = {
  ...agencyBlueprint,
  views: [
    { key: 'progress_board', name: '進度看板', type: 'board', groupBy: 'state', columns: [], sort: [] },
    {
      key: 'client_view',
      name: '給客戶看',
      type: 'table',
      columns: ['title', 'state', 'client_signoff_date'],
      sort: [],
      filter: { itemTypes: ['requirement', 'change_request'], states: [], stateCategories: [] },
      visibleTo: ['client', 'project_manager'],
    },
  ],
  automations: [
    {
      key: 'mr_merged_to_internal_qa',
      name: '合併請求合併後轉內部驗收',
      enabled: true,
      trigger: { source: 'gitlab', event: 'merge_request.merged' },
      conditions: [{ field: 'state', in: ['developing'] }],
      actions: [{ type: 'transition', to: 'internal_qa' }],
    },
  ],
  bindings: [{ connector: 'gitlab', resource: 'example-group/web-app' }],
};
