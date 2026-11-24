import { describe, expect, it } from 'vitest';
import type { Workflow } from '../../../api/blueprint';
import { DEFAULT_MY_WORK_FILTERS } from '../../../domain/myWorkView';
import { agyItem } from '../../../test/fixtures';
import { agencyBlueprint } from '../../../test/blueprints';
import { boardCategories, columnItems, compareCards, groupedMoveTargets, planDrop } from './boardColumns';

const delivery = agencyBlueprint.workflows.find((workflow) => workflow.key === 'delivery') as Workflow;

describe('boardCategories', () => {
  it('未結束的狀態：待規劃、未開始、進行中，加上放下去的目標「完成」', () => {
    // Arrange / Act
    const categories = boardCategories(DEFAULT_MY_WORK_FILTERS.status, []);

    // Assert
    expect(categories).toEqual(['backlog', 'not_started', 'in_progress', 'done']);
  });

  it('所有狀態是五欄；一個類別是那一欄；某一個狀態是它的類別那一欄', () => {
    // Arrange / Act / Assert
    expect(boardCategories({ kind: 'all' }, [])).toEqual(['backlog', 'not_started', 'in_progress', 'done', 'canceled']);
    expect(boardCategories({ kind: 'category', category: 'canceled' }, [])).toEqual(['canceled']);
    expect(boardCategories({ kind: 'state', state: 'client_qa' }, ['in_progress'])).toEqual(['in_progress']);
  });

  it('某一個狀態的類別還不知道（專案內容沒讀到）時沒有欄', () => {
    // Arrange / Act / Assert
    expect(boardCategories({ kind: 'state', state: 'client_qa' }, [])).toEqual([]);
  });
});

describe('boardCategories：專案的流程與畫面上的卡片', () => {
  it('選了專案：流程沒有的類別不畫；完成照舊（流程有完成的狀態時）', () => {
    // Arrange / Act
    const categories = boardCategories(DEFAULT_MY_WORK_FILTERS.status, [], { projectCategories: ['not_started', 'in_progress', 'done'] });

    // Assert
    expect(categories).toEqual(['not_started', 'in_progress', 'done']);
  });

  it('專案的流程沒有完成的狀態：也沒有放下去的目標', () => {
    // Arrange / Act / Assert
    expect(boardCategories(DEFAULT_MY_WORK_FILTERS.status, [], { projectCategories: ['not_started', 'in_progress'] })).toEqual(['not_started', 'in_progress']);
  });

  it('畫面上的卡片所在的類別一定有欄，照 STATE_CATEGORIES 的順序', () => {
    // Arrange / Act
    const categories = boardCategories(DEFAULT_MY_WORK_FILTERS.status, [], { occupied: ['canceled', 'done'] });

    // Assert
    expect(categories).toEqual(['backlog', 'not_started', 'in_progress', 'done', 'canceled']);
  });
});

describe('compareCards', () => {
  it('到期日早的在前，沒有到期日的最後，同一天依專案代號再依編號', () => {
    // Arrange
    const items = [
      agyItem({ number: 1, dueDate: null }),
      agyItem({ number: 3, dueDate: '2026-11-20' }),
      agyItem({ number: 2, dueDate: '2026-11-20' }),
      agyItem({ number: 9, dueDate: '2026-11-18' }),
    ];

    // Act
    const sorted = items.toSorted(compareCards).map((item) => item.key);

    // Assert
    expect(sorted).toEqual(['AGY-9', 'AGY-2', 'AGY-3', 'AGY-1']);
  });
});

describe('planDrop', () => {
  const inQa = agyItem({ number: 3, state: 'internal_qa', stateCategory: 'in_progress' });
  const confirming = agyItem({ number: 4 });

  it('放回自己的類別不改（同一個類別裡換狀態用「移到…」）', () => {
    // Arrange / Act / Assert
    expect(planDrop(delivery, inQa, 'in_progress', 'project_manager')).toEqual({ kind: 'ignore' });
  });

  it('那個類別只有一個走得到的狀態就直接移；走不到的不算', () => {
    // Arrange
    const quoting = agyItem({ number: 6, state: 'quoting' });

    // Act
    const plan = planDrop(delivery, quoting, 'in_progress', 'editor');

    // Assert
    expect(plan).toEqual({ kind: 'move', to: 'designing' });
  });

  it('有好幾個走得到的狀態就要問，列出那個類別的全部狀態', () => {
    // Arrange
    const free: Workflow = {
      key: 'free',
      name: '自由',
      states: [
        { key: 'todo', name: '待辦', category: 'not_started' },
        { key: 'doing', name: '進行中', category: 'in_progress' },
        { key: 'waiting', name: '等客戶', category: 'in_progress' },
      ],
    };

    // Act
    const plan = planDrop(free, agyItem({ state: 'todo' }), 'in_progress', 'editor');

    // Assert
    expect(plan.kind).toBe('choose');
    expect(plan.kind === 'choose' ? plan.targets.map((target) => target.state.key) : []).toEqual(['doing', 'waiting']);
  });

  it('一個都走不到：帶著原因，照樣送給後端說明（以後端為準）', () => {
    // Arrange / Act
    const plan = planDrop(delivery, confirming, 'done', 'editor');

    // Assert
    expect(plan).toEqual({ kind: 'blocked', to: 'closed', availability: { kind: 'no_path' } });
  });

  it('角色不夠時說角色（而不是流程沒有這條路）', () => {
    // Arrange
    const clientQa = agyItem({ number: 8, state: 'client_qa', stateCategory: 'in_progress' });

    // Act
    const forEditor = planDrop(delivery, clientQa, 'done', 'editor');
    const forManager = planDrop(delivery, clientQa, 'done', 'project_manager');

    // Assert
    expect(forEditor).toEqual({ kind: 'blocked', to: 'closed', availability: { kind: 'role', roles: ['project_manager'] } });
    expect(forManager).toEqual({ kind: 'move', to: 'closed' });
  });

  it('流程裡沒有那個類別的狀態', () => {
    // Arrange / Act
    const plan = planDrop(delivery, confirming, 'canceled', 'admin');

    // Assert
    expect(plan).toEqual({ kind: 'empty' });
  });

  it('還不知道流程（專案內容沒讀到）時不處理', () => {
    // Arrange / Act / Assert
    expect(planDrop(undefined, confirming, 'in_progress', null)).toEqual({ kind: 'ignore' });
  });
});

describe('groupedMoveTargets', () => {
  it('走得到的在前、依類別分組；走不到的另成一組', () => {
    // Arrange
    const item = agyItem({ number: 5, state: 'developing', stateCategory: 'in_progress' });

    // Act
    const groups = groupedMoveTargets(delivery, item, 'editor');

    // Assert
    expect(groups.map(({ target, startsGroup }) => [target.state.key, startsGroup])).toEqual([
      ['internal_qa', false],
      ['confirming', true],
      ['quoting', false],
      ['designing', true],
      ['client_qa', false],
      ['closed', true],
    ]);
  });
});

describe('columnItems', () => {
  const todo = agyItem({ number: 1 });
  const other = agyItem({ number: 2, state: 'developing', stateCategory: 'in_progress' });

  it('只畫自己類別的卡片', () => {
    // Arrange / Act
    const items = columnItems([todo, other], 'in_progress', [], () => undefined);

    // Assert
    expect(items.map((item) => item.key)).toEqual(['AGY-2']);
  });

  it('送出中的卡片從原欄拿掉、畫在目標的欄，帶著新的狀態與類別', () => {
    // Arrange
    const pending = [{ item: todo, to: 'developing' }];

    // Act
    const source = columnItems([todo, other], 'not_started', pending, () => 'in_progress');
    const target = columnItems([todo, other], 'in_progress', pending, () => 'in_progress');

    // Assert
    expect(source).toEqual([]);
    expect(target.map((item) => [item.key, item.state, item.stateCategory])).toEqual([
      ['AGY-1', 'developing', 'in_progress'],
      ['AGY-2', 'developing', 'in_progress'],
    ]);
  });

  it('目標的類別不明（藍圖沒讀到）或沒有那一欄時，卡片不畫在任何欄', () => {
    // Arrange
    const pending = [{ item: todo, to: 'developing' }];

    // Act
    const columns = (['not_started', 'in_progress'] as const).map((category) => columnItems([todo], category, pending, () => undefined));

    // Assert
    expect(columns).toEqual([[], []]);
  });
});
