import { describe, expect, it } from 'vitest';
import type { ItemTypeSummary } from '../../api/types';
import { DEFAULT_MY_WORK_FILTERS, type MyWorkFilters } from '../../domain/myWorkView';
import { agyDetail, agyProject, smkDetail, webProject } from '../../test/fixtures';
import { hasTypeChoice, projectOptions, pruneToProject, statusFromValue, statusOptions, statusValueOf, typeOptions } from './myWorkChoices';

const summaries: readonly ItemTypeSummary[] = [
  { key: 'bug', names: ['缺陷', '問題'], projectKeys: ['AGY', 'SMK'] },
  { key: 'task', names: ['任務'], projectKeys: ['SMK'] },
];

function filters(patch: Partial<MyWorkFilters>): MyWorkFilters {
  return { ...DEFAULT_MY_WORK_FILTERS, ...patch };
}

describe('statusValueOf / statusFromValue', () => {
  it('每一種狀態條件寫成下拉的值再讀回來都一樣', () => {
    // Arrange
    const statuses = [{ kind: 'open' }, { kind: 'all' }, { kind: 'category', category: 'done' }, { kind: 'state', state: 'review' }] as const;

    // Act / Assert
    for (const status of statuses) {
      expect(statusFromValue(statusValueOf(status))).toEqual(status);
    }
  });

  it('不認得的值當成預設', () => {
    // Arrange / Act / Assert
    expect(statusFromValue('category:nothing')).toEqual({ kind: 'open' });
  });
});

describe('typeOptions', () => {
  it('跨專案：依 key 合併，名稱不同時用「／」接起來', () => {
    // Arrange / Act
    const options = typeOptions(summaries, undefined, DEFAULT_MY_WORK_FILTERS);

    // Assert
    expect(options.map((option) => option.label)).toEqual(['所有類型', '缺陷／問題', '任務']);
  });

  it('選了專案：那個專案的類型', () => {
    // Arrange / Act
    const options = typeOptions(summaries, smkDetail(), filters({ project: 'SMK' }));

    // Assert
    expect(options.map((option) => option.value)).toEqual(['', ...smkDetail().blueprint.itemTypes.map((itemType) => itemType.key)]);
  });

  it('網址上的類型不在清單裡時補上它；只有一種類型時不放', () => {
    // Arrange
    const one = [{ key: 'task', names: ['任務'], projectKeys: ['SMK'] }];

    // Act
    const plain = typeOptions(one, undefined, DEFAULT_MY_WORK_FILTERS);
    const withUnknown = typeOptions(one, undefined, filters({ type: 'gone' }));

    // Assert
    expect(hasTypeChoice(plain, DEFAULT_MY_WORK_FILTERS)).toBe(false);
    expect(withUnknown.at(-1)).toMatchObject({ value: 'gone' });
    expect(hasTypeChoice(withUnknown, filters({ type: 'gone' }))).toBe(true);
  });
});

describe('statusOptions', () => {
  it('沒選專案：預設、所有狀態與五個類別', () => {
    // Arrange / Act
    const options = statusOptions(DEFAULT_MY_WORK_FILTERS, undefined);

    // Assert
    expect(options.map((option) => option.value)).toEqual([
      'open',
      'all',
      'category:backlog',
      'category:not_started',
      'category:in_progress',
      'category:done',
      'category:canceled',
    ]);
  });

  it('選了專案：後面接那個專案的狀態，各一條分隔線', () => {
    // Arrange / Act
    const options = statusOptions(filters({ project: 'SMK' }), smkDetail());

    // Assert
    expect(options.find((option) => option.value === 'category:backlog')?.startsGroup).toBe(true);
    const firstState = options.find((option) => option.value.startsWith('state:'));
    expect(firstState?.startsGroup).toBe(true);
  });

  it('選了有兩個流程的專案、沒選類型：狀態依流程分組，組名自己有線、不再加分隔線', () => {
    // Arrange / Act
    const options = statusOptions(filters({ project: 'AGY' }), agyDetail());

    // Assert
    const states = options.filter((option) => option.value.startsWith('state:'));
    expect(states.filter((option) => option.groupLabel !== undefined).map((option) => option.groupLabel)).toEqual(['需求、需求變更單', '任務']);
    expect(states.some((option) => option.startsGroup === true)).toBe(false);
  });

  it('同 key 不同名稱的第二個狀態：選了讀回來是那個 key', () => {
    // Arrange / Act
    const status = statusFromValue('state:planning\u0000campaign_flow');

    // Assert
    expect(status).toEqual({ kind: 'state', state: 'planning' });
  });
});

describe('projectOptions', () => {
  it('所有專案，再列沒有封存的專案；網址上指定的專案不在清單裡時補上代號', () => {
    // Arrange
    const archived = { ...agyProject, archivedAt: '2026-11-01T00:00:00Z' };

    // Act
    const options = projectOptions([webProject, archived], 'ZZZ');

    // Assert
    expect(options.map((option) => option.value)).toEqual(['', 'WEB', 'ZZZ']);
  });
});

describe('pruneToProject', () => {
  it('沒選專案、或專案內容還沒讀到：照原樣（同一個物件）', () => {
    // Arrange
    const typed = filters({ type: 'task' });

    // Act / Assert
    expect(pruneToProject(typed, smkDetail())).toBe(typed);
    expect(pruneToProject(filters({ project: 'SMK', type: 'zzz' }), undefined).type).toBe('zzz');
  });

  it('類型不在那個專案裡：拿掉；狀態不在那個專案的流程裡：換回預設', () => {
    // Arrange
    const typed = filters({ project: 'SMK', type: 'zzz', status: { kind: 'state', state: 'nothing' } });

    // Act
    const pruned = pruneToProject(typed, smkDetail());

    // Assert
    expect(pruned).toEqual(filters({ project: 'SMK' }));
  });

  it('都在那個專案裡：同一個物件', () => {
    // Arrange
    const detail = smkDetail();
    const type = detail.blueprint.itemTypes[0]?.key ?? '';
    const typed = filters({ project: 'SMK', type });

    // Act / Assert
    expect(pruneToProject(typed, detail)).toBe(typed);
  });
});
