import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../api/blueprint';
import { exampleBlueprint } from '../test/fixtures';
import { itemTypeName, stateLabel } from './blueprint';
import { formatDate, formatShortDate, isOverdue, sortByDueDate, toCalendarDate } from './dates';
import { projectRoleLabel } from './labels';

describe('stateLabel', () => {
  it('用藍圖裡這個類型的流程的狀態名稱', () => {
    // Arrange / Act
    const label = stateLabel(exampleBlueprint, 'requirement', 'internal_qa', 'in_progress');

    // Assert
    expect(label).toBe('內部驗收');
  });

  it('沒有藍圖時退回狀態類別的中文', () => {
    // Arrange / Act
    const label = stateLabel(undefined, 'task', 'doing', 'in_progress');

    // Assert
    expect(label).toBe('進行中');
  });

  it('藍圖裡找不到這個狀態時也退回狀態類別', () => {
    // Arrange
    const blueprint: ProjectBlueprint = { ...exampleBlueprint, itemTypes: [] };

    // Act
    const label = stateLabel(blueprint, 'task', 'doing', 'done');

    // Assert
    expect(label).toBe('完成');
  });
});

describe('itemTypeName', () => {
  it('找得到時用名稱，找不到時用 key', () => {
    // Arrange / Act / Assert
    expect(itemTypeName(exampleBlueprint, 'task')).toBe('任務');
    expect(itemTypeName(exampleBlueprint, 'unknown_type')).toBe('unknown_type');
  });
});

describe('projectRoleLabel', () => {
  it('內建角色用中文', () => {
    // Arrange / Act / Assert
    expect(projectRoleLabel('admin')).toBe('管理員');
    expect(projectRoleLabel('viewer')).toBe('檢視者');
  });

  it('自訂角色用藍圖裡的名稱，沒有藍圖時用 key', () => {
    // Arrange / Act / Assert
    expect(projectRoleLabel('project_manager', exampleBlueprint.roles)).toBe('專案經理');
    expect(projectRoleLabel('project_manager')).toBe('project_manager');
  });

  it('沒有角色時說明不是成員', () => {
    // Arrange / Act / Assert
    expect(projectRoleLabel(null)).toBe('未加入');
  });
});

describe('isOverdue', () => {
  it('到期日在今天之前、項目還沒結束時算過期', () => {
    // Arrange / Act / Assert
    expect(isOverdue('2026-11-12', 'in_progress', '2026-11-13')).toBe(true);
  });

  it('今天到期不算過期', () => {
    // Arrange / Act / Assert
    expect(isOverdue('2026-11-13', 'in_progress', '2026-11-13')).toBe(false);
  });

  it('已經完成或取消的項目不算過期', () => {
    // Arrange / Act / Assert
    expect(isOverdue('2026-11-01', 'done', '2026-11-13')).toBe(false);
    expect(isOverdue('2026-11-01', 'canceled', '2026-11-13')).toBe(false);
  });

  it('沒有到期日不算過期', () => {
    // Arrange / Act / Assert
    expect(isOverdue(null, 'not_started', '2026-11-13')).toBe(false);
  });
});

describe('dates', () => {
  it('用本地時間取日期', () => {
    // Arrange
    const localMorning = new Date(2026, 0, 5, 8, 30);

    // Act / Assert
    expect(toCalendarDate(localMorning)).toBe('2026-01-05');
  });

  it('顯示用斜線分隔', () => {
    // Arrange / Act / Assert
    expect(formatDate('2026-11-13')).toBe('2026/11/13');
  });

  it('簡短的寫法：今年的只寫月與日，別的年份照完整的寫', () => {
    // Arrange
    const todayDate = '2026-11-27';

    // Act / Assert
    expect(formatShortDate('2026-11-05', todayDate)).toBe('11/05');
    expect(formatShortDate('2025-12-31', todayDate)).toBe('2025/12/31');
    expect(formatShortDate('2027-01-02', todayDate)).toBe('2027/01/02');
  });
});

describe('sortByDueDate', () => {
  it('依到期日排，沒有到期日的放最後，不改動原本的陣列', () => {
    // Arrange
    const items = [
      { key: 'A', dueDate: null },
      { key: 'B', dueDate: '2026-12-01' },
      { key: 'C', dueDate: '2026-11-01' },
    ];

    // Act
    const sorted = sortByDueDate(items);

    // Assert
    expect(sorted.map((item) => item.key)).toEqual(['C', 'B', 'A']);
    expect(items.map((item) => item.key)).toEqual(['A', 'B', 'C']);
  });
});
