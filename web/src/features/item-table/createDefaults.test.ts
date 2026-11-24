import { describe, expect, it } from 'vitest';
import { EMPTY_FILTERS } from '../../domain/itemFilters';
import { cycleOf, milestoneOf } from '../../test/planning';
import { agyDetail, exampleOwner, memberAsEditor, ownerAsAdmin, smkItem } from '../../test/fixtures';
import { simpleTasksBlueprint } from '../../test/blueprints';
import { assigneeFromFilter, defaultItemType, fieldDefaultsFromFilters, itemDefaultsFromFilters, matchesTableFilters } from './createDefaults';

const TODAY = '2026-11-18';
const me = exampleOwner.personId;
const detail = agyDetail({ members: [ownerAsAdmin, memberAsEditor] });

describe('新增時沿用的條件', () => {
  it('類型：篩選的類型是這個專案有的就用它，否則用第一個類型', () => {
    // Arrange & Act & Assert
    expect(defaultItemType(detail, 'task')).toBe('task');
    expect(defaultItemType(detail, null)).toBe('requirement');
    expect(defaultItemType(detail, 'unknown')).toBe('requirement');
    expect(defaultItemType(agyDetail({ blueprint: { ...detail.blueprint, itemTypes: [] } }), null)).toBe('');
  });

  it('負責人：「我」與成員照帶；沒有負責人、不是成員的人、自己不是成員時不帶', () => {
    // Arrange & Act & Assert
    expect(assigneeFromFilter(detail, 'me', me)).toBe(me);
    expect(assigneeFromFilter(detail, memberAsEditor.personId, me)).toBe(memberAsEditor.personId);
    expect(assigneeFromFilter(detail, null, me)).toBeUndefined();
    expect(assigneeFromFilter(detail, 'unassigned', me)).toBeUndefined();
    expect(assigneeFromFilter(detail, 'someone-else', me)).toBeUndefined();
    expect(assigneeFromFilter(agyDetail({ members: [memberAsEditor] }), 'me', me)).toBeUndefined();
  });

  it('符不符合表格的條件：類型、狀態、負責人、搜尋都要對', () => {
    // Arrange
    const item = smkItem({ number: 2, title: '新的任務', state: 'todo' });

    // Act & Assert
    expect(matchesTableFilters(item, EMPTY_FILTERS, me, TODAY, 'monday')).toBe(true);
    expect(matchesTableFilters(item, { ...EMPTY_FILTERS, type: 'task', state: 'todo', assignee: 'me', q: '任務' }, me, TODAY, 'monday')).toBe(true);
    expect(matchesTableFilters(item, { ...EMPTY_FILTERS, type: 'bug' }, me, TODAY, 'monday')).toBe(false);
    expect(matchesTableFilters(item, { ...EMPTY_FILTERS, state: 'doing' }, me, TODAY, 'monday')).toBe(false);
    expect(matchesTableFilters(item, { ...EMPTY_FILTERS, assignee: 'unassigned' }, me, TODAY, 'monday')).toBe(false);
    expect(matchesTableFilters(item, { ...EMPTY_FILTERS, q: '首頁' }, me, TODAY, 'monday')).toBe(false);
    expect(matchesTableFilters(item, { ...EMPTY_FILTERS, q: 'smk-2' }, me, TODAY, 'monday')).toBe(true);
  });
});

describe('itemDefaultsFromFilters：優先度與到期日', () => {
  it('篩了優先度就帶那個優先度；「未設定」（none）不帶', () => {
    // Arrange & Act & Assert
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, due: null, priority: 'high' }, TODAY, [])).toEqual({ priority: 'high' });
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, due: null, priority: 'none' }, TODAY, [])).toEqual({});
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, due: null, priority: null }, TODAY, [])).toEqual({});
  });

  it('只有「今天到期」帶到期日（今天）；已過期、本週、沒有到期日不預設', () => {
    // Arrange & Act & Assert
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, due: 'today', priority: null }, TODAY, [])).toEqual({ dueDate: TODAY });
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, due: 'overdue', priority: null }, TODAY, [])).toEqual({});
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, due: 'week', priority: null }, TODAY, [])).toEqual({});
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, due: 'none', priority: null }, TODAY, [])).toEqual({});
  });
});

describe('itemDefaultsFromFilters：週期與里程碑', () => {
  const SPRINT = '0190a000-0000-7000-8000-0000000000d2';
  const V1 = '0190a000-0000-7000-8000-0000000000e1';
  const cycles = [cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT })];
  const milestones = [milestoneOf('v1.0 上線', '2026-12-20', { id: V1 })];

  it('篩了週期（含目前的）或里程碑就帶那一個', () => {
    // Arrange & Act & Assert
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, cycle: 'current' }, TODAY, cycles, milestones)).toEqual({ cycleId: SPRINT });
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, cycle: SPRINT, milestone: V1 }, TODAY, cycles, milestones)).toEqual({
      cycleId: SPRINT,
      milestoneId: V1,
    });
  });

  it('網址指著已刪除的週期、里程碑時不帶：不在已載入的清單裡', () => {
    // Arrange
    const gone = '0190a000-0000-7000-8000-0000000000ff';

    // Act & Assert
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, cycle: gone, milestone: gone }, TODAY, cycles, milestones)).toEqual({});
  });

  it('沒有週期、沒有里程碑、沒有目前的週期都不帶', () => {
    // Arrange & Act & Assert
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, cycle: 'none', milestone: 'none' }, TODAY, cycles)).toEqual({});
    expect(itemDefaultsFromFilters({ ...EMPTY_FILTERS, cycle: 'current' }, '2027-01-01', cycles)).toEqual({});
  });

  it('新的項目符不符合週期與里程碑的條件：目前的週期、沒有週期', () => {
    // Arrange
    const inSprint = smkItem({ number: 2, cycleId: SPRINT });

    // Act & Assert
    expect(matchesTableFilters(inSprint, { ...EMPTY_FILTERS, cycle: 'current' }, me, TODAY, 'monday', cycles)).toBe(true);
    expect(matchesTableFilters(inSprint, { ...EMPTY_FILTERS, cycle: 'none' }, me, TODAY, 'monday', cycles)).toBe(false);
    expect(matchesTableFilters(inSprint, { ...EMPTY_FILTERS, milestone: V1 }, me, TODAY, 'monday', cycles)).toBe(false);
  });
});

describe('fieldDefaultsFromFilters：人員欄位', () => {
  const members = [ownerAsAdmin, memberAsEditor];
  const stranger = '0190a000-0000-7000-8000-0000000000aa';

  it('篩的是成員（含「我」換成自己）就帶；不是成員、手打的值不帶', () => {
    // Arrange / Act
    const defaults = (reviewer: string, list = members) =>
      fieldDefaultsFromFilters(simpleTasksBlueprint, { reviewer }, 'task', me, list);

    // Assert
    expect(defaults(memberAsEditor.personId)).toEqual({ reviewer: memberAsEditor.personId });
    expect(defaults('me')).toEqual({ reviewer: me });
    expect(defaults(stranger)).toEqual({});
    expect(defaults('me', [memberAsEditor])).toEqual({});
  });
});
