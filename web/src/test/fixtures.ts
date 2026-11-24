import type { ProjectBlueprint } from '../api/blueprint';
import type { Item, Member, Project, ProjectDetail, SessionUser, Template } from '../api/types';
import { toCalendarDate } from '../domain/dates';
import { agencyBlueprint, simpleTasksBlueprint } from './blueprints';

export const exampleOwner: SessionUser = {
  personId: '0190a000-0000-7000-8000-000000000001',
  displayName: '範例使用者',
  email: 'user@example.com',
  organizationId: '0190a000-0000-7000-8000-0000000000a1',
  organizationName: '範例組織',
  role: 'owner',
};

export const exampleMember: SessionUser = {
  ...exampleOwner,
  personId: '0190a000-0000-7000-8000-000000000002',
  displayName: '範例成員',
  email: 'member@example.com',
  role: 'member',
};

/** 內容同 schemas/blueprint/v0/examples/agency-project.blueprint.yaml 的主要部分。 */
export const exampleBlueprint: ProjectBlueprint = {
  apiVersion: 'blueprint/v0',
  kind: 'ProjectBlueprint',
  metadata: {
    key: 'web',
    name: '官網改版',
    basedOn: { template: 'agency_project', version: '1.0.0' },
  },
  fields: [
    { key: 'quote_amount', name: '報價金額', type: 'currency', currency: 'TWD' },
    { key: 'client_signoff_date', name: '客戶驗收日', type: 'date' },
  ],
  workflows: [
    {
      key: 'delivery',
      name: '交付流程',
      states: [
        { key: 'confirming', name: '需求確認', category: 'not_started' },
        { key: 'developing', name: '開發', category: 'in_progress' },
        { key: 'internal_qa', name: '內部驗收', category: 'in_progress' },
        { key: 'closed', name: '結案', category: 'done' },
      ],
      transitions: [
        { from: 'confirming', to: 'developing' },
        { from: 'developing', to: 'internal_qa' },
        { from: 'internal_qa', to: 'closed', allowedRoles: ['project_manager'] },
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
    },
    { key: 'task', name: '任務', workflow: 'task_flow', parents: ['requirement'] },
  ],
  roles: [
    {
      key: 'project_manager',
      name: '專案經理',
      permissions: ['item.read', 'item.create', 'item.update', 'item.transition'],
    },
  ],
};

export const webProject: Project = {
  id: '0190a000-0000-7000-8000-0000000000b1',
  key: 'WEB',
  name: '官網改版',
  blueprintVersion: 1,
  myRole: 'admin',
  createdAt: '2026-11-01T02:00:00Z',
  archivedAt: null,
};

export const appProject: Project = {
  id: '0190a000-0000-7000-8000-0000000000b2',
  key: 'APP',
  name: '會員 App',
  blueprintVersion: 3,
  myRole: 'project_manager',
  createdAt: '2026-11-02T02:00:00Z',
  archivedAt: null,
};

export const exampleTemplates: readonly Template[] = [
  {
    key: 'agency_project',
    name: '接案專案',
    description: '從需求確認到結案的交付流程。',
    workflows: [{ name: '交付流程', itemTypes: ['需求', '需求變更單'], states: ['需求確認', '報價', '製作', '結案'], stateCategories: ['not_started', 'not_started', 'in_progress', 'done'], canceledStates: [] }],
    parents: [],
    fields: ['報價金額', '客戶驗收日'],
    exampleCount: 6,
  },
  {
    key: 'simple_tasks',
    name: '簡單任務',
    description: '待辦、進行中、完成。',
    workflows: [{ name: '任務流程', itemTypes: ['任務'], states: ['待辦', '進行中', '完成'], stateCategories: ['not_started', 'in_progress', 'done'], canceledStates: ['不處理'] }],
    parents: [{ itemType: '任務', parents: ['任務'] }],
    fields: [],
    exampleCount: 4,
  },
];

export function projectDetail(project: Project = webProject): ProjectDetail {
  return {
    project,
    blueprint: exampleBlueprint,
    members: [{ personId: exampleOwner.personId, displayName: '範例使用者', role: 'admin' }],
    myPermissions: ['item.read', 'item.create', 'item.update', 'item.transition'],
  };
}

export function makeItem(overrides: Partial<Item> = {}): Item {
  const number = overrides.number ?? 1;
  const projectKey = overrides.projectKey ?? 'WEB';
  return {
    key: `${projectKey}-${String(number)}`,
    projectKey,
    number,
    type: 'task',
    title: '首頁設計稿',
    state: 'todo',
    stateCategory: 'not_started',
    assignee: { personId: exampleOwner.personId, displayName: exampleOwner.displayName },
    priority: 'none',
    startDate: null,
    dueDate: null,
    estimate: null,
    parentKey: null,
    cycleId: null,
    milestoneId: null,
    fields: {},
    createdAt: '2026-11-03T02:00:00Z',
    updatedAt: '2026-11-03T02:00:00Z',
    ...overrides,
  };
}

/** 專案管理員在專案裡的全部權限（和真的後端回的 myPermissions 相同）。 */
export const ALL_PROJECT_PERMISSIONS: ProjectDetail['myPermissions'] = [
  'item.read',
  'item.create',
  'item.update',
  'item.transition',
  'item.delete',
  'comment.create',
  'blueprint.read',
  'blueprint.propose',
  'blueprint.apply',
  'project.manage',
];

export const ownerAsAdmin: Member = {
  personId: exampleOwner.personId,
  displayName: exampleOwner.displayName,
  role: 'admin',
};

export const memberAsEditor: Member = {
  personId: exampleMember.personId,
  displayName: exampleMember.displayName,
  role: 'editor',
};

/** 用模板 simple_tasks 開的專案，和本機測試環境的 SMK 同樣的設定。 */
export const smkProject: Project = {
  id: '0190a000-0000-7000-8000-0000000000c1',
  key: 'SMK',
  name: '冒煙測試',
  blueprintVersion: 1,
  myRole: 'admin',
  createdAt: '2026-11-12T16:54:59Z',
  archivedAt: null,
};

/** 用模板 agency_project 開的專案，和本機測試環境的 AGY 同樣的設定。 */
export const agyProject: Project = {
  id: '0190a000-0000-7000-8000-0000000000c2',
  key: 'AGY',
  name: '接案測試',
  blueprintVersion: 1,
  myRole: 'admin',
  createdAt: '2026-11-12T16:55:11Z',
  archivedAt: null,
};

export function smkDetail(overrides: Partial<ProjectDetail> = {}): ProjectDetail {
  return {
    project: smkProject,
    blueprint: simpleTasksBlueprint,
    members: [ownerAsAdmin, memberAsEditor],
    myPermissions: ALL_PROJECT_PERMISSIONS,
    ...overrides,
  };
}

export function agyDetail(overrides: Partial<ProjectDetail> = {}): ProjectDetail {
  return {
    project: agyProject,
    blueprint: agencyBlueprint,
    members: [ownerAsAdmin, memberAsEditor],
    myPermissions: ALL_PROJECT_PERMISSIONS,
    ...overrides,
  };
}

/**
 * 沒有能篩選的自訂欄位的接案專案（單選、多選、人員、勾選都拿掉）：工具列的「更多條件」只在有週期、里程碑或收起來的條件時才出現，
 * 專案有能篩的欄位時它一律在（docs/design/08-interaction.md 8.3），測工具列怎麼收的測試要用這一個。
 */
export function agyDetailWithoutFieldFilters(): ProjectDetail {
  const detail = agyDetail();
  const keep = new Set(['currency', 'date', 'long_text']);
  return { ...detail, blueprint: { ...detail.blueprint, fields: detail.blueprint.fields.filter((field) => keep.has(field.type)) } };
}

export function smkItem(overrides: Partial<Item> = {}): Item {
  return makeItem({ projectKey: 'SMK', type: 'task', ...overrides });
}

export function agyItem(overrides: Partial<Item> = {}): Item {
  return makeItem({
    projectKey: 'AGY',
    type: 'requirement',
    state: 'confirming',
    stateCategory: 'not_started',
    assignee: null,
    ...overrides,
  });
}

/** 以今天為準往前或往後幾天的日期，測試「過期」時不必固定系統時間。 */
export function daysFromToday(days: number): string {
  const now = new Date();
  return toCalendarDate(new Date(now.getFullYear(), now.getMonth(), now.getDate() + days));
}
