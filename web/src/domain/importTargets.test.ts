import { describe, expect, it } from 'vitest';
import { agencyBlueprint } from '../test/blueprints';
import { assignTarget, autoMapColumns, importTargets, normalizeHeader } from './importTargets';

const targets = importTargets(agencyBlueprint);

describe('autoMapColumns', () => {
  it('標題等於欄位名稱時對應，不分大小寫與前後空白', () => {
    // Arrange & Act
    const mapping = autoMapColumns([' 標題 ', '類型', '負責人', '優先度'], targets);

    // Assert
    expect(mapping).toEqual(['title', 'type', 'assignee', 'priority']);
  });

  it('英文別名與底線、空白的寫法都能對應', () => {
    // Arrange & Act
    const mapping = autoMapColumns(['Title', 'Status', 'Start Date', 'due_date', 'Estimate', 'Parent', 'Description', 'Sprint', 'Milestone'], targets);

    // Assert
    expect(mapping).toEqual(['title', 'state', 'startDate', 'dueDate', 'estimate', 'parent', 'description', 'cycle', 'milestone']);
  });

  it('自訂欄位用藍圖的名稱或 key 對應', () => {
    // Arrange & Act
    const mapping = autoMapColumns(['報價金額', 'client_signoff_date'], targets);

    // Assert
    expect(mapping).toEqual(['field:quote_amount', 'field:client_signoff_date']);
  });

  it('一個目標只給一欄，後面重複的欄位不匯入', () => {
    // Arrange & Act
    const mapping = autoMapColumns(['標題', 'Title', '說明'], targets);

    // Assert
    expect(mapping).toEqual(['title', null, 'description']);
  });

  it('認不得的標題與空白的標題不匯入', () => {
    // Arrange & Act
    const mapping = autoMapColumns(['備註欄', ''], targets);

    // Assert
    expect(mapping).toEqual([null, null]);
  });

  it('被排除的目標不自動對應', () => {
    // Arrange & Act
    const mapping = autoMapColumns(['標題', '狀態'], targets, new Set(['state']));

    // Assert
    expect(mapping).toEqual(['title', null]);
  });

  it('上層項目的標題（含全形括號的完整名稱）也能對應', () => {
    // Arrange & Act
    const mapping = autoMapColumns(['上層項目（編號）'], targets);

    // Assert
    expect(mapping).toEqual(['parent']);
  });
});

describe('assignTarget', () => {
  it('另一欄選了同一個目標時，原本那一欄換成不匯入', () => {
    // Arrange
    const mapping = ['title', 'description', null] as const;

    // Act
    const next = assignTarget(mapping, 2, 'title');

    // Assert
    expect(next).toEqual([null, 'description', 'title']);
    expect(mapping).toEqual(['title', 'description', null]);
  });

  it('改成不匯入只動那一欄', () => {
    // Arrange & Act
    const next = assignTarget(['title', 'type'], 0, null);

    // Assert
    expect(next).toEqual([null, 'type']);
  });
});

describe('normalizeHeader', () => {
  it('全形字與大小寫統一', () => {
    expect(normalizeHeader('Ｔｉｔｌｅ ')).toBe('title');
  });
});
