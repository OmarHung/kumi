import { describe, expect, it } from 'vitest';
import { agencyBlueprint, simpleTasksBlueprint } from '../test/blueprints';
import type { CsvRecord } from './csv';
import { convertRows, type ImportContext } from './importRows';
import type { ColumnMapping } from './importTargets';

const CONTEXT: ImportContext = {
  blueprint: agencyBlueprint,
  members: [{ personId: 'p1', displayName: '王小明', email: 'ming@example.com' }],
  cycles: [{ id: 'c1', name: '衝刺 1' }],
  milestones: [{ id: 'm1', name: '上線' }],
  defaultType: 'requirement',
};

function record(line: number, ...cells: string[]): CsvRecord {
  return { line, cells };
}

describe('convertRows', () => {
  it('每一種欄位都轉成伺服器要的值', () => {
    // Arrange
    const mapping: ColumnMapping = ['title', 'type', 'state', 'assignee', 'priority', 'startDate', 'dueDate', 'estimate', 'parent', 'description', 'cycle', 'milestone', 'field:quote_amount', 'field:client_signoff_date'];
    const row = record(2, '官網', '需求', '開發', 'ming@example.com', '高', '2026/11/1', '2026-11-30', '8', 'agy-3', '第一行\n第二行', '衝刺 1', '上線', 'NT$ 12,000', '2026/12/1');

    // Act
    const { rows, issues } = convertRows([row], mapping, CONTEXT);

    // Assert
    expect(issues).toEqual([]);
    expect(rows[0]?.request).toEqual({
      type: 'requirement',
      title: '官網',
      state: 'developing',
      assigneeId: 'p1',
      priority: 'high',
      startDate: '2026-11-01',
      dueDate: '2026-11-30',
      estimate: 8,
      parentKey: 'AGY-3',
      description: '第一行\n第二行',
      cycleId: 'c1',
      milestoneId: 'm1',
      fields: { quote_amount: 12000, client_signoff_date: '2026-12-01' },
    });
  });

  it('空白的格子是不填，沒有類型欄位時用預設的類型', () => {
    // Arrange & Act
    const { rows } = convertRows([record(2, '官網', '', '')], ['title', 'assignee', 'dueDate'], CONTEXT);

    // Assert
    expect(rows[0]?.request).toEqual({ type: 'requirement', title: '官網' });
  });

  it('轉不了的格子是那一列的問題，說明列號與欄位', () => {
    // Arrange
    const records = [record(2, '甲', '王小明'), record(5, '乙', '王小名')];

    // Act
    const { rows, issues } = convertRows(records, ['title', 'assignee'], CONTEXT);

    // Assert
    expect(rows.map((row) => row.line)).toEqual([2]);
    expect(issues).toEqual([{ line: 5, field: '負責人', message: '找不到「王小名」這個成員' }]);
  });

  it('標題空白時說明，其他欄位照樣檢查', () => {
    // Arrange & Act
    const { issues } = convertRows([record(3, '', '很多')], ['title', 'estimate'], CONTEXT);

    // Assert
    expect(issues).toEqual([
      { line: 3, field: '標題', message: '標題是空白的' },
      { line: 3, field: '估計', message: '「很多」不是數字' },
    ]);
  });

  it('一列有好幾個問題時全部列出', () => {
    // Arrange & Act
    const { issues } = convertRows([record(2, '甲', '高很多', '2026/13/1')], ['title', 'priority', 'dueDate'], CONTEXT);

    // Assert
    expect(issues.map((issue) => issue.field)).toEqual(['優先度', '到期日']);
  });

  it('有類型欄位時每一列用自己的類型；找不到類型的列只報類型的問題', () => {
    // Arrange
    const records = [record(2, '甲', '任務', '待辦'), record(3, '乙', '史詩', '待辦'), record(4, '丙', '', '待辦')];

    // Act
    const { rows, issues } = convertRows(records, ['title', 'type', 'state'], CONTEXT);

    // Assert
    expect(rows[0]?.request).toEqual({ type: 'task', title: '甲', state: 'todo' });
    expect(issues).toEqual([
      { line: 3, field: '類型', message: '找不到「史詩」這個類型' },
      { line: 4, field: '類型', message: '類型是空白的' },
    ]);
  });

  it('狀態要在那個類型的流程裡', () => {
    // Arrange & Act
    const { issues } = convertRows([record(2, '甲', '任務', '開發')], ['title', 'type', 'state'], CONTEXT);

    // Assert
    expect(issues).toEqual([{ line: 2, field: '狀態', message: '「開發」不是「任務」流程裡的狀態' }]);
  });

  it('類型沒有的自訂欄位有填值時是問題', () => {
    // Arrange & Act
    const { issues } = convertRows([record(2, '甲', '任務', '1000')], ['title', 'type', 'field:quote_amount'], CONTEXT);

    // Assert
    expect(issues).toEqual([{ line: 2, field: '報價金額', message: '「任務」這個類型沒有這個欄位' }]);
  });

  it('自訂欄位依型別轉換：單選、多選、勾選、成員、文字', () => {
    // Arrange
    const context: ImportContext = { ...CONTEXT, blueprint: simpleTasksBlueprint, defaultType: 'task' };
    const mapping: ColumnMapping = ['title', 'field:category', 'field:tags', 'field:blocked', 'field:reviewer', 'field:summary', 'field:budget'];

    // Act
    const { rows, issues } = convertRows([record(2, '甲', '錯誤', '前端、後端', '是', '王小明', '摘要', '$1,000')], mapping, context);

    // Assert
    expect(issues).toEqual([]);
    expect(rows[0]?.request.fields).toEqual({
      category: 'bug',
      tags: ['frontend', 'backend'],
      blocked: true,
      reviewer: 'p1',
      summary: '摘要',
      budget: 1000,
    });
  });

  it('預覽放轉好的值（名稱而不是 key）', () => {
    // Arrange & Act
    const { rows } = convertRows([record(2, '甲', 'task', 'todo', 'urgent')], ['title', 'type', 'state', 'priority'], CONTEXT);

    // Assert
    expect(Object.fromEntries(rows[0]?.preview ?? [])).toEqual({ title: '甲', type: '任務', state: '待辦', priority: '緊急' });
  });

  it('不匯入的欄位完全不看', () => {
    // Arrange & Act
    const { rows, issues } = convertRows([record(2, '甲', '亂寫的')], ['title', null], CONTEXT);

    // Assert
    expect(issues).toEqual([]);
    expect(rows).toHaveLength(1);
  });

  it('標題含換行的列是問題', () => {
    // Arrange & Act
    const { issues } = convertRows([record(2, '甲\n乙')], ['title'], CONTEXT);

    // Assert
    expect(issues).toEqual([{ line: 2, field: '標題', message: '不能有換行或控制字元' }]);
  });
});
