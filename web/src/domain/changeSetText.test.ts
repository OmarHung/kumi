import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../api/blueprint';
import type { Change, ChangeResource, Impact } from '../api/types';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import { blueprintChangeDetails, describeBlueprintChange } from './changeSetText';
import { describeImpact, impactLines, itemsTouched } from './impactText';

const before = simpleTasksBlueprint;
const after: ProjectBlueprint = {
  ...before,
  fields: [
    { key: 'category', name: '類別', type: 'single_select', options: [{ key: 'feature', name: '功能' }, { key: 'urgent', name: '急件' }] },
    { key: 'risk', name: '風險', type: 'long_text' },
    { key: 'budget', name: '預算', type: 'currency', currency: 'USD' },
    { key: 'summary', name: '摘要', type: 'single_select', options: [{ key: 'short', name: '短' }] },
  ],
  workflows: [
    {
      key: 'task_flow',
      name: '任務流程',
      states: [
        { key: 'todo', name: '待處理', category: 'backlog' },
        { key: 'review', name: '審查中', category: 'in_progress' },
        { key: 'done', name: '完成', category: 'in_progress' },
      ],
    },
  ],
  itemTypes: [{ key: 'task', name: '任務', workflow: 'task_flow', fields: ['category', 'risk'] }],
};
const context = { before, after };

function change(overrides: Partial<Change>): Change {
  return { kind: 'modified', resource: 'state', key: 'todo', parentKey: 'task_flow', changedProperties: [], ...overrides };
}

describe('差異轉成句子', () => {
  it.each([
    [change({ kind: 'added', key: 'review' }), '新增狀態「審查中」'],
    [change({ kind: 'removed', key: 'doing' }), '刪除狀態「進行中」'],
    [change({ changedProperties: ['name'] }), '把狀態「待辦」改名為「待處理」'],
    [change({ key: 'done', changedProperties: ['category'] }), '修改狀態「完成」：類別從「完成」改成「進行中」'],
    [change({ key: 'todo', changedProperties: ['name', 'category'] }), '修改狀態「待辦」：改名為「待處理」、類別從「未開始」改成「待規劃」'],
    [change({ resource: 'workflow', key: 'task_flow', parentKey: null, changedProperties: ['states'] }), '修改流程「任務流程」：調整了狀態的順序'],
    [change({ resource: 'field', kind: 'added', key: 'risk', parentKey: null }), '新增欄位「風險」（長文字）'],
    [change({ resource: 'field', kind: 'removed', key: 'notes', parentKey: null }), '刪除欄位「備註」'],
    [change({ resource: 'field', key: 'category', parentKey: null, changedProperties: ['name'] }), '把欄位「分類」改名為「類別」'],
    [change({ resource: 'field_option', kind: 'added', key: 'urgent', parentKey: 'category' }), '在欄位「分類」新增選項「急件」'],
    [change({ resource: 'field_option', key: 'feature', parentKey: 'category', changedProperties: ['name', 'color'] }), '修改欄位「分類」的選項「新功能」：改名為「功能」、顏色有調整'],
    [change({ resource: 'item_type', key: 'task', parentKey: null, changedProperties: ['fields'] }), '修改類型「任務」：多了欄位「風險」，少了欄位「標籤」、「檢查日」、「點數」、「預算」、「卡住了」、「參考連結」、「審查者」、「摘要」、「備註」'],
    [change({ resource: 'blueprint', key: 'smk', parentKey: null, changedProperties: ['fields', 'views'] }), '調整了欄位、檢視的順序'],
    [change({ resource: 'view', kind: 'removed', key: 'board', parentKey: null }), '刪除檢視「board」'],
    [change({ resource: 'field', key: 'budget', parentKey: null, changedProperties: ['currency'] }), '修改欄位「預算」：幣別從「TWD」改成「USD」'],
    [change({ resource: 'field', key: 'summary', parentKey: null, changedProperties: ['type', 'options'] }), '修改欄位「摘要」：型別從「文字」改成「單選」、加上了選項'],
    [change({ resource: 'field', key: 'category', parentKey: null, changedProperties: ['options'] }), '修改欄位「分類」：調整了選項的順序'],
    [change({ resource: 'view', key: 'board', parentKey: null, changedProperties: ['type'] }), '修改檢視「board」：顯示方式有調整'],
  ])('%#', (input, expected) => {
    // Arrange & Act & Assert
    expect(describeBlueprintChange(input, context)).toBe(expected);
  });

  // 伺服器比對的每一種資源的屬性（PmTool.Blueprint 的 BlueprintDiffer，對照 project-blueprint.schema.json）。
  const COMPARED_PROPERTIES: readonly (readonly [ChangeResource, readonly string[]])[] = [
    ['metadata', ['name', 'description', 'basedOn']],
    ['field', ['name', 'type', 'description', 'currency', 'options']],
    ['field_option', ['name', 'color']],
    ['workflow', ['name', 'states', 'transitions']],
    ['state', ['name', 'category']],
    ['transition', ['allowedRoles', 'requireFields']],
    ['item_type', ['name', 'workflow', 'fields', 'parents', 'icon']],
    ['view', ['name', 'type', 'groupBy', 'columns', 'sort', 'filter', 'visibleTo']],
    ['automation', ['name', 'enabled', 'trigger', 'conditions', 'actions']],
    ['role', ['name', 'permissions']],
    ['blueprint', ['fields', 'workflows', 'itemTypes', 'views', 'automations', 'roles', 'bindings']],
  ];

  it.each(COMPARED_PROPERTIES.flatMap(([resource, properties]) => properties.map((property) => [resource, property] as const)))(
    '%s 的 %s 有中文的說法',
    (resource, property) => {
      // Arrange
      const input = change({ resource, key: 'zz', parentKey: 'zz', changedProperties: [property] });

      // Act
      const text = describeBlueprintChange(input, context);

      // Assert
      expect(text).not.toContain(property);
    },
  );

  it('轉換用兩端的狀態名稱；有好幾個流程時狀態後面帶流程的名稱', () => {
    // Arrange
    const agyContext = { before: agencyBlueprint, after: agencyBlueprint };

    // Act & Assert
    expect(
      describeBlueprintChange(change({ resource: 'transition', kind: 'removed', key: 'developing->internal_qa', parentKey: 'delivery' }), agyContext),
    ).toBe('刪除轉換「開發」→「內部驗收」（交付流程）');
    expect(describeBlueprintChange(change({ kind: 'added', key: 'doing', parentKey: 'task_flow' }), agyContext)).toBe(
      '新增狀態「進行中」（任務流程）',
    );
  });
});

describe('資料影響轉成句子', () => {
  function impact(value: Impact): Impact {
    return value;
  }

  it.each([
    [impact({ kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'review', affected: 12, blocksApply: false }), '12 個項目會從「進行中」搬到「審查中」'],
    [impact({ kind: 'field_drop', field: 'notes', affected: 1234, blocksApply: false }), '1,234 個項目的「備註」會被封存'],
    [
      impact({ kind: 'field_conversion', field: 'risk', fromType: 'text', toType: 'long_text', onFailure: 'leave_empty', affected: 3, blocksApply: false }),
      '3 個項目的「風險」會從「文字」轉成「長文字」，轉不過去的會清空（原本的值封存起來）',
    ],
    [
      impact({ kind: 'field_conversion', field: 'risk', fromType: 'text', toType: 'number', onFailure: 'abort', affected: 3, blocksApply: true }),
      '3 個項目的「風險」會從「文字」轉成「數字」，有轉不過去的就整份不套用',
    ],
    [impact({ kind: 'item_type_removal', itemType: 'task', affected: 4, blocksApply: true }), '還有 4 個「任務」：要先在項目頁刪除這些項目或改成別的類型，才能刪除這個類型'],
    [impact({ kind: 'item_type_removal', itemType: 'task', affected: 0, blocksApply: false }), '沒有「任務」，可以直接刪除這個類型'],
    [impact({ kind: 'parent_removal', itemType: 'task', parent: 'task', affected: 2, blocksApply: true }), '還有 2 個「任務」掛在「任務」底下，要先移開'],
    [impact({ kind: 'role_removal', role: 'client', affected: 1, blocksApply: true }), '還有 1 位成員是「client」角色，要先換成別的角色'],
    [impact({ kind: 'field_detach', itemType: 'task', field: 'summary', affected: 5, blocksApply: false }), '5 個「任務」的「摘要」會被封存'],
  ])('%#', (input, expected) => {
    // Arrange & Act & Assert
    expect(describeImpact(input, context, true)).toBe(expected);
  });

  it('不是等待核准的變更集不再算數量，只說會做什麼', () => {
    // Arrange & Act & Assert
    expect(describeImpact({ kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'review', affected: 0, blocksApply: false }, context, false)).toBe(
      '停在「進行中」的項目搬到「審查中」',
    );
    expect(describeImpact({ kind: 'field_drop', field: 'notes', affected: 0, blocksApply: false }, context, false)).toBe('「備註」的值封存起來');
    expect(describeImpact({ kind: 'role_removal', role: 'client', affected: 0, blocksApply: false }, context, false)).toBe('刪除角色「client」');
    expect(describeImpact({ kind: 'field_detach', itemType: 'task', field: 'summary', affected: 0, blocksApply: false }, context, false)).toBe(
      '「摘要」從「任務」拿掉，值封存起來',
    );
  });

  it('會動到的項目：搬移、封存（包含從類型拿掉的欄位）、轉換的數量加總，其他的不算', () => {
    // Arrange
    const impacts: Impact[] = [
      { kind: 'state_move', workflow: 'task_flow', from: 'doing', to: 'review', affected: 12, blocksApply: false },
      { kind: 'field_drop', field: 'notes', affected: 8, blocksApply: false },
      { kind: 'field_detach', itemType: 'task', field: 'summary', affected: 5, blocksApply: false },
      { kind: 'role_removal', role: 'client', affected: 3, blocksApply: true },
    ];

    // Act & Assert
    expect(itemsTouched(impacts)).toBe(25);
  });
});

describe('轉換、類型、角色的差異轉成句子', () => {
  const withView: ProjectBlueprint = {
    ...agencyBlueprint,
    views: [{ key: 'client_view', name: '給客戶看', type: 'table', visibleTo: ['client', 'project_manager'] }],
  };
  function withClosing(allowedRoles: readonly string[], requireFields: readonly string[]): ProjectBlueprint {
    const workflows = agencyBlueprint.workflows.map((workflow) => ({
      ...workflow,
      ...(workflow.transitions && {
        transitions: workflow.transitions.map((transition) => (transition.to === 'closed' ? { ...transition, allowedRoles, requireFields } : transition)),
      }),
    }));
    return { ...withView, workflows };
  }

  const closing = (changedProperties: readonly string[]) =>
    change({ resource: 'transition', key: 'client_qa->closed', parentKey: 'delivery', changedProperties });

  it.each([
    [withClosing(['project_manager', 'client'], ['client_signoff_date']), ['allowedRoles'], '修改轉換「客戶驗收」→「結案」（交付流程）：允許的角色改成「專案經理」、「客戶」'],
    [withClosing([], ['client_signoff_date']), ['allowedRoles'], '修改轉換「客戶驗收」→「結案」（交付流程）：改成不限角色'],
    [withClosing(['project_manager'], ['description', 'client_signoff_date']), ['requireFields'], '修改轉換「客戶驗收」→「結案」（交付流程）：要先填的欄位改成「說明」、「客戶驗收日」'],
    [withClosing(['admin'], []), ['allowedRoles', 'requireFields'], '修改轉換「客戶驗收」→「結案」（交付流程）：允許的角色改成「管理員」；改成不必先填任何欄位'],
  ])('%#', (after, properties, expected) => {
    // Arrange & Act & Assert
    expect(describeBlueprintChange(closing(properties), { before: withView, after })).toBe(expected);
  });

  it('類型換流程、改上層；角色改權限；檢視看得到的角色', () => {
    // Arrange
    const after: ProjectBlueprint = {
      ...withView,
      itemTypes: agencyBlueprint.itemTypes.map((type) => {
        if (type.key === 'requirement') return { ...type, workflow: 'task_flow' };
        return type.key === 'task' ? { ...type, parents: ['change_request'] } : type;
      }),
      roles: [
        { key: 'project_manager', name: '專案經理', permissions: ['item.read', 'item.delete', 'blueprint.read'] },
        { key: 'client', name: '業主', permissions: ['comment.create', 'item.read'] },
      ],
      views: [{ key: 'client_view', name: '給客戶看', type: 'table', visibleTo: ['client'] }],
    };
    const emptied: ProjectBlueprint = { ...after, itemTypes: after.itemTypes.map((type) => ({ ...type, parents: [] })), views: [{ key: 'client_view', name: '給客戶看', type: 'table' }] };
    const types = (key: string, changedProperties: readonly string[]) => change({ resource: 'item_type', key, parentKey: null, changedProperties });
    const roles = (key: string, changedProperties: readonly string[]) => change({ resource: 'role', key, parentKey: null, changedProperties });
    const view = change({ resource: 'view', key: 'client_view', parentKey: null, changedProperties: ['visibleTo'] });
    const agyContext = { before: withView, after };

    // Act & Assert
    expect(describeBlueprintChange(types('requirement', ['workflow']), agyContext)).toBe('修改類型「需求」：流程從「交付流程」改成「任務流程」');
    expect(describeBlueprintChange(types('task', ['parents']), agyContext)).toBe('修改類型「任務」：可以掛的上層類型改成「需求變更單」');
    expect(describeBlueprintChange(types('task', ['parents']), { before: withView, after: emptied })).toBe('修改類型「任務」：不能掛在任何類型底下');
    expect(describeBlueprintChange(roles('project_manager', ['permissions']), agyContext)).toBe(
      '修改角色「專案經理」：權限多了「刪除項目」，少了「新增項目」、「修改項目」、「改變狀態」、「留言」、「提出設定修改」、「套用設定修改」',
    );
    expect(describeBlueprintChange(roles('client', ['name', 'permissions']), agyContext)).toBe('修改角色「客戶」：改名為「業主」；調整了權限的順序');
    expect(describeBlueprintChange(change({ resource: 'role', kind: 'added', key: 'client', parentKey: null }), agyContext)).toBe('新增角色「業主」，權限：查看項目、留言');
    expect(describeBlueprintChange(view, agyContext)).toBe('修改檢視「給客戶看」：看得到的角色改成「客戶」');
    expect(describeBlueprintChange(view, { before: withView, after: emptied })).toBe('修改檢視「給客戶看」：改成所有成員都看得到');
  });

  it('類型換流程的搬移：舊狀態在別的流程時，兩邊都帶流程的名稱', () => {
    // Arrange
    const after: ProjectBlueprint = { ...agencyBlueprint, itemTypes: agencyBlueprint.itemTypes.map((type) => (type.key === 'task' ? { ...type, workflow: 'delivery' } : type)) };
    const move: Impact = { kind: 'state_move', workflow: 'delivery', from: 'done', to: 'closed', affected: 3, blocksApply: false };

    // Act & Assert
    expect(describeImpact(move, { before: agencyBlueprint, after }, true)).toBe('3 個項目會從「完成」（任務流程）搬到「結案」（交付流程）');
    expect(describeImpact(move, { before: agencyBlueprint, after }, false)).toBe('停在「完成」（任務流程）的項目搬到「結案」（交付流程）');
  });
});

describe('預覽頁要讓核准的人看得到內容', () => {
  const restricted: ProjectBlueprint = {
    ...before,
    workflows: [
      {
        ...before.workflows[0],
        key: 'task_flow',
        name: '任務流程',
        states: before.workflows[0]?.states ?? [],
        transitions: [
          { from: 'todo', to: 'doing', allowedRoles: [], requireFields: [] },
          { from: 'doing', to: 'done', allowedRoles: ['editor'], requireFields: ['description', 'summary'] },
        ],
      },
    ],
  };
  const startRestricting = change({ resource: 'workflow', key: 'task_flow', parentKey: null, changedProperties: ['transitions'] });

  it('不限制切成限制：列出每一條可以走的轉換與它的限制', () => {
    // Arrange & Act
    const text = describeBlueprintChange(startRestricting, { before, after: restricted });

    // Assert
    expect(text).toBe('修改流程「任務流程」：開始限制轉換，只能走「待辦」→「進行中」、「進行中」→「完成」（限「編輯者」；要先填「說明」、「摘要」）');
    expect(blueprintChangeDetails(startRestricting, { before, after: restricted })).toEqual([]);
    expect(describeBlueprintChange(startRestricting, { before: restricted, after: before })).toBe('修改流程「任務流程」：不再限制轉換');
  });

  it('切成限制的轉換很多時，句子只說幾條，每一條另外一行', () => {
    // Arrange
    const states = ['a', 'b', 'c'].map((key) => ({ key, name: key.toUpperCase(), category: 'in_progress' as const }));
    const transitions = states.flatMap((from) => states.filter((to) => to.key !== from.key).map((to) => ({ from: from.key, to: to.key })));
    const after: ProjectBlueprint = { ...before, workflows: [{ key: 'task_flow', name: '任務流程', states, transitions }] };
    const context = { before, after };

    // Act & Assert
    expect(describeBlueprintChange(startRestricting, context)).toBe('修改流程「任務流程」：開始限制轉換，只能走下面這 6 條');
    expect(blueprintChangeDetails(startRestricting, context)).toEqual(['「A」→「B」', '「A」→「C」', '「B」→「A」', '「B」→「C」', '「C」→「A」', '「C」→「B」']);
    expect(blueprintChangeDetails(change({ changedProperties: ['name'] }), context)).toEqual([]);
  });

  it('切成限制、一條轉換都沒列：說一條路都沒有', () => {
    // Arrange
    const after: ProjectBlueprint = { ...before, workflows: [{ key: 'task_flow', name: '任務流程', states: [], transitions: [] }] };

    // Act & Assert
    expect(describeBlueprintChange(startRestricting, { before, after })).toBe('修改流程「任務流程」：開始限制轉換，一條轉換都沒有');
  });

  it('新增的轉換帶上它的限制；沒有限制時只說新增', () => {
    // Arrange
    const added: ProjectBlueprint = {
      ...agencyBlueprint,
      workflows: agencyBlueprint.workflows.map((workflow) =>
        workflow.key === 'delivery'
          ? { ...workflow, transitions: [...(workflow.transitions ?? []), { from: 'designing', to: 'internal_qa', allowedRoles: ['project_manager'], requireFields: ['description'] }, { from: 'quoting', to: 'closed' }] }
          : workflow,
      ),
    };
    const context = { before: agencyBlueprint, after: added };
    const transition = (key: string) => change({ resource: 'transition', kind: 'added', key, parentKey: 'delivery' });

    // Act & Assert
    expect(describeBlueprintChange(transition('designing->internal_qa'), context)).toBe('新增轉換「設計」→「內部驗收」（交付流程），限「專案經理」；要先填「說明」');
    expect(describeBlueprintChange(transition('quoting->closed'), context)).toBe('新增轉換「報價」→「結案」（交付流程）');
  });

  it('新增的角色帶上權限（照固定的順序）；一個都沒有時說沒有任何權限', () => {
    // Arrange
    const after: ProjectBlueprint = {
      ...agencyBlueprint,
      roles: [
        { key: 'qa_lead', name: 'QA 主管', permissions: ['blueprint.read', 'item.update', 'item.read', 'item.transition', 'comment.create'] },
        { key: 'nobody', name: '旁觀', permissions: [] },
      ],
    };
    const role = (key: string) => change({ resource: 'role', kind: 'added', key, parentKey: null });

    // Act & Assert
    expect(describeBlueprintChange(role('qa_lead'), { before: agencyBlueprint, after })).toBe('新增角色「QA 主管」，權限：查看項目、修改項目、改變狀態、留言、查看設定');
    expect(describeBlueprintChange(role('nobody'), { before: agencyBlueprint, after })).toBe('新增角色「旁觀」，沒有任何權限');
  });

  it('檢視的 visibleTo 是空陣列：一個角色都沒列，不是所有成員都看得到', () => {
    // Arrange
    const withView: ProjectBlueprint = { ...agencyBlueprint, views: [{ key: 'client_view', name: '給客戶看', type: 'table', visibleTo: ['client'] }] };
    const emptied: ProjectBlueprint = { ...withView, views: [{ key: 'client_view', name: '給客戶看', type: 'table', visibleTo: [] }] };
    const view = change({ resource: 'view', key: 'client_view', parentKey: null, changedProperties: ['visibleTo'] });

    // Act & Assert
    expect(describeBlueprintChange(view, { before: withView, after: emptied })).toBe('修改檢視「給客戶看」：看得到的角色改成一個都沒列');
  });
});

describe('影響的清單', () => {
  const move = (from: string, affected: number): Impact => ({ kind: 'state_move', workflow: 'task_flow', from, to: 'review', affected, blocksApply: false });

  it('等待核准時，沒有項目要搬的狀態合成一行，放在搬移的最後', () => {
    // Arrange
    const impacts: Impact[] = [move('todo', 0), move('doing', 4), move('done', 0), { kind: 'field_drop', field: 'notes', affected: 2, blocksApply: false }];

    // Act
    const lines = impactLines(impacts, context, true);

    // Assert
    expect(lines.map((line) => line.text)).toEqual([
      '4 個項目會從「進行中」搬到「審查中」',
      '其他 2 個狀態沒有項目要搬：「待辦」、「完成」',
      '2 個項目的「備註」會被封存',
    ]);
    expect(lines.map((line) => line.blocksApply)).toEqual([false, false, false]);
  });

  it('每一個搬移都是 0 時不說「其他」；不是等待核准時照舊一項一行', () => {
    // Arrange
    const impacts: Impact[] = [move('todo', 0), move('done', 0), { kind: 'role_removal', role: 'client', affected: 1, blocksApply: true }];

    // Act & Assert
    expect(impactLines(impacts, context, true)).toEqual([
      { text: '2 個狀態沒有項目要搬：「待辦」、「完成」', blocksApply: false },
      { text: '還有 1 位成員是「client」角色，要先換成別的角色', blocksApply: true },
    ]);
    expect(impactLines(impacts, context, false).map((line) => line.text)).toEqual([
      '停在「待辦」的項目搬到「審查中」',
      '停在「完成」的項目搬到「審查中」',
      '刪除角色「client」',
    ]);
  });
});

describe('搬移的舊狀態有歧義時帶流程的名稱', () => {
  const [delivery, taskFlow] = agencyBlueprint.workflows;
  const todo = { key: 'todo', name: '待辦', category: 'not_started' } as const;
  const withTodo = (workflow: typeof delivery) => (workflow === undefined ? [] : [{ ...workflow, states: [todo, ...workflow.states] }]);

  it('刪除的狀態在別的流程也有同一個 key：舊狀態帶流程的名稱', () => {
    // Arrange
    const base: ProjectBlueprint = { ...agencyBlueprint, workflows: [...withTodo(delivery), ...(taskFlow ? [taskFlow] : [])] };
    const impact: Impact = { kind: 'state_move', workflow: 'delivery', from: 'todo', to: 'confirming', affected: 2, blocksApply: false };

    // Act & Assert
    expect(describeImpact(impact, { before: base, after: agencyBlueprint }, true)).toBe('2 個項目會從「待辦」（交付流程）搬到「需求確認」');
  });

  it('類型換流程、舊狀態的 key 在好幾個流程都有：只帶真的會搬過來的那個流程', () => {
    // Arrange
    const support = { key: 'support', name: '支援流程', states: [{ key: 'open', name: '受理', category: 'not_started' }] } as const;
    const base: ProjectBlueprint = { ...agencyBlueprint, workflows: [...withTodo(delivery), ...(taskFlow ? [taskFlow] : []), support] };
    const after: ProjectBlueprint = {
      ...base,
      itemTypes: agencyBlueprint.itemTypes.map((type) => (type.key === 'requirement' ? { ...type, workflow: 'support' } : type)),
    };
    const impact: Impact = { kind: 'state_move', workflow: 'support', from: 'todo', to: 'open', affected: 3, blocksApply: false };

    // Act & Assert
    expect(describeImpact(impact, { before: base, after }, true)).toBe('3 個項目會從「待辦」（交付流程）搬到「受理」（支援流程）');
  });

  it('類型換流程、新流程又刪除了同 key 的狀態：同一條搬移的兩個來源都列出來', () => {
    // Arrange
    const base: ProjectBlueprint = { ...agencyBlueprint, workflows: [...withTodo(delivery), ...(taskFlow ? [taskFlow] : [])] };
    const after: ProjectBlueprint = {
      ...agencyBlueprint,
      workflows: [...(delivery ? [delivery] : []), ...(taskFlow ? [{ ...taskFlow, states: taskFlow.states.filter((state) => state.key !== 'todo') }] : [])],
      itemTypes: agencyBlueprint.itemTypes.map((type) => (type.key === 'requirement' ? { ...type, workflow: 'task_flow' } : type)),
    };
    const impact: Impact = { kind: 'state_move', workflow: 'task_flow', from: 'todo', to: 'doing', affected: 3, blocksApply: false };

    // Act & Assert
    expect(describeImpact(impact, { before: base, after }, true)).toBe('3 個項目會從「待辦」（交付流程）、「待辦」（任務流程）搬到「進行中」（任務流程）');
  });
});
