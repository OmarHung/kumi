import { describe, expect, it } from 'vitest';
import type { FieldChange } from '../api/types';
import { commented, created, deletedComment, migrated, transitioned, updated } from '../test/activity';
import { simpleTasksBlueprint } from '../test/blueprints';
import { memberAsEditor, ownerAsAdmin } from '../test/fixtures';
import { cycleOf, milestoneOf } from '../test/planning';
import { activityAnnouncement, changeText, describeChange, UNKNOWN_PERSON } from './activity';

const SPRINT_2 = '0190a000-0000-7000-8000-0000000000d2';
const SPRINT_3 = '0190a000-0000-7000-8000-0000000000d3';
const V1 = '0190a000-0000-7000-8000-0000000000e1';

const context = {
  blueprint: simpleTasksBlueprint,
  members: [ownerAsAdmin, memberAsEditor],
  typeKey: 'task',
  cycles: [cycleOf('衝刺 2', '2026-11-16', '2026-11-29', { id: SPRINT_2 }), cycleOf('衝刺 3', '2026-11-30', '2026-12-13', { id: SPRINT_3 })],
  milestones: [milestoneOf('v1.0 上線', '2026-12-20', { id: V1 })],
};

function sentence(field: string, from: FieldChange['from'], to: FieldChange['to']): string {
  return changeText(describeChange({ field, from, to }, context));
}

describe('週期與里程碑的修改', () => {
  it.each([
    ['週期換一個：用目前的名稱', 'cycle', SPRINT_2, SPRINT_3, '把「週期」從 衝刺 2 改成 衝刺 3'],
    ['排進週期', 'cycle', null, SPRINT_2, '把「週期」設為 衝刺 2'],
    ['移出週期', 'cycle', SPRINT_2, null, '清除了「週期」（原本是 衝刺 2）'],
    ['週期已經刪除', 'cycle', 'gone', SPRINT_2, '把「週期」從 已刪除的週期 改成 衝刺 2'],
    ['里程碑', 'milestone', null, V1, '把「里程碑」設為 v1.0 上線'],
    ['里程碑已經刪除', 'milestone', 'gone', null, '清除了「里程碑」（原本是 已刪除的里程碑）'],
  ] as const)('%s', (_name, field, from, to, expected) => {
    // Act & Assert
    expect(sentence(field, from, to)).toBe(expected);
  });

  it('週期的清單還沒載入時不猜，用刪節號', () => {
    // Arrange
    const loading = { ...context, cycles: undefined };

    // Act
    const text = changeText(describeChange({ field: 'cycle', from: null, to: SPRINT_2 }, loading));

    // Assert
    expect(text).toBe('把「週期」設為 …');
  });

  it('清單讀不到（null）時用中性的說法，不說成已刪除；刪節號只用在載入中', () => {
    // Arrange
    const failed = { ...context, cycles: null, milestones: null };

    // Act & Assert
    expect(changeText(describeChange({ field: 'cycle', from: SPRINT_2, to: null }, failed))).toBe('清除了「週期」（原本是 某個週期）');
    expect(changeText(describeChange({ field: 'milestone', from: null, to: V1 }, failed))).toBe('把「里程碑」設為 某個里程碑');
  });
});

describe('一個欄位的修改', () => {
  it.each([
    ['日期從一天改到另一天', 'due_date', '2026-11-20', '2026-11-30', '把「到期日」從 2026/11/20 改成 2026/11/30'],
    ['從沒有到有', 'start_date', null, '2026-11-01', '把「開始日」設為 2026/11/01'],
    ['清掉值', 'due_date', '2026-11-20', null, '清除了「到期日」（原本是 2026/11/20）'],
    ['標題', 'title', '舊標題', '新標題', '把「標題」從 舊標題 改成 新標題'],
    ['負責人用成員名稱', 'assignee', ownerAsAdmin.personId, memberAsEditor.personId, '把「負責人」從 範例使用者 改成 範例成員'],
    ['查不到的人用替代文字', 'assignee', 'someone-else', null, `清除了「負責人」（原本是 ${UNKNOWN_PERSON}）`],
    ['優先度用中文，none 算沒有值', 'priority', 'none', 'high', '把「優先度」設為 高'],
    ['優先度改回未設定是清掉', 'priority', 'urgent', 'none', '清除了「優先度」（原本是 緊急）'],
    ['估計照數字的寫法', 'estimate', 1234.5, 2, '把「估計」從 1,234.5 改成 2'],
    ['上層項目用編號', 'parent', null, 'SMK-1', '把「上層項目」設為 SMK-1'],
    ['說明只說有改', 'description', null, null, '修改了「說明」'],
    ['單選用選項名稱', 'category', 'feature', 'bug', '把「分類」從 新功能 改成 錯誤'],
    ['多選用選項名稱', 'tags', ['frontend'], ['frontend', 'docs'], '把「標籤」從 前端 改成 前端、文件'],
    ['成員欄位用成員名稱', 'reviewer', null, memberAsEditor.personId, '把「審查者」設為 範例成員'],
    ['成員欄位查不到的人', 'reviewer', null, 'someone-else', `把「審查者」設為 ${UNKNOWN_PERSON}`],
    ['金額欄位帶幣別', 'budget', null, 1500, '把「預算」設為 $1,500'],
    ['長文字只說有改', 'notes', null, null, '修改了「備註」'],
    ['勾選', 'blocked', null, true, '勾選了「卡住了」'],
    ['取消勾選', 'blocked', true, null, '取消勾選「卡住了」'],
    ['藍圖裡已經沒有的欄位用 key 與原始值', 'legacy', 'a', ['b', 'c'], '把「legacy」從 a 改成 b、c'],
  ] as const)('%s', (_name, field, from, to, expected) => {
    // Act & Assert
    expect(sentence(field, from, to)).toBe(expected);
  });

  it('修改的值分成文字與值兩種片段，畫面可以把值標出來', () => {
    // Act
    const segments = describeChange({ field: 'due_date', from: '2026-11-20', to: '2026-11-30' }, context).segments;

    // Assert
    expect(segments).toEqual([
      { kind: 'text', text: '把「到期日」從 ' },
      { kind: 'value', text: '2026/11/20' },
      { kind: 'text', text: ' 改成 ' },
      { kind: 'value', text: '2026/11/30' },
    ]);
  });
});

describe('新的一筆活動給螢幕報讀器的摘要', () => {
  it.each([
    ['建立', created(), '範例使用者 建立了這個項目'],
    ['改了一個欄位', updated([{ field: 'title', from: 'A', to: 'B' }]), '範例使用者 把「標題」從 A 改成 B'],
    [
      '改了好幾個欄位只說幾個，不逐一唸',
      updated([
        { field: 'title', from: 'A', to: 'B' },
        { field: 'description', from: null, to: null },
      ]),
      '範例使用者 修改了 2 個欄位',
    ],
    ['改狀態用藍圖的名稱', transitioned('todo', 'doing'), '範例使用者 把狀態從「待辦」改成「進行中」'],
    ['藍圖裡沒有的狀態用 key', transitioned('todo', 'archived'), '範例使用者 把狀態從「待辦」改成「archived」'],
    ['短的留言照唸', commented('看起來不錯'), '範例使用者 留言：看起來不錯'],
    ['刪除的留言（內容是 null）不唸「留言：」加空字串', deletedComment(), '範例使用者 留言（已刪除）'],
    [
      '套用設定變更時搬了狀態',
      migrated(4, { fromState: 'review', toState: 'done' }),
      '範例使用者 套用設定變更（第 4 版）時，狀態從「review」搬到「完成」',
    ],
    [
      '套用設定變更時動到好幾件事只說幾件',
      migrated(5, { fromState: 'review', toState: 'done', changes: [{ field: 'notes', from: null, to: null }] }),
      '範例使用者 套用設定變更（第 5 版）時，調整了 2 件事',
    ],
    ['沒有版本時只說套用設定變更', migrated(1, { blueprintVersion: null, changes: [{ field: 'summary', from: null, to: 'A' }] }), '範例使用者 套用設定變更時，「摘要」從 空白 轉成 A'],
  ] as const)('%s', (_name, activity, expected) => {
    // Act & Assert
    expect(activityAnnouncement(activity, context)).toBe(expected);
  });

  it('長的留言只唸開頭 40 個字，換行當成空白', () => {
    // Arrange
    const activity = commented(`第一行\n\n${'很'.repeat(60)}`);

    // Act
    const text = activityAnnouncement(activity, context);

    // Assert
    expect(text).toBe(`範例使用者 留言：第一行 ${'很'.repeat(36)}…`);
  });

  it('截斷時不會把一個表情符號切成兩半', () => {
    // Arrange
    const activity = commented(`${'好'.repeat(39)}👍🏽之後的字`);

    // Act
    const text = activityAnnouncement(activity, context);

    // Assert
    expect(text).toBe(`範例使用者 留言：${'好'.repeat(39)}👍🏽…`);
  });

  it('很長的修改（例如很長的標題）也截短', () => {
    // Arrange
    const activity = updated([{ field: 'title', from: '短', to: '長'.repeat(200) }]);

    // Act
    const text = activityAnnouncement(activity, context);

    // Assert
    expect(text).toBe(`範例使用者 把「標題」從 短 改成 ${'長'.repeat(68)}…`);
  });
});
