import { describe, expect, it } from 'vitest';
import type { ProjectBlueprint } from '../api/blueprint';
import { agencyBlueprint } from '../test/blueprints';
import { agyItem, memberAsEditor, ownerAsAdmin } from '../test/fixtures';
import {
  childrenNote,
  parentSentence,
  previewTypeChange,
  stateSentence,
  type TypeChangeContext,
  type TypeChangeKnowledge,
} from './itemTypeChange';

const members = [ownerAsAdmin, memberAsEditor];
const nothingKnown: TypeChangeKnowledge = { parentType: null, children: [], hasMoreChildren: false };

/** 有「改變狀態」權限的人。 */
function canMove(blueprint: ProjectBlueprint = agencyBlueprint): TypeChangeContext {
  return { blueprint, members, canTransition: true };
}

/** 需求變更單可以掛在需求底下：測上層的類型對不對得上。 */
const changeUnderRequirement: ProjectBlueprint = {
  ...agencyBlueprint,
  itemTypes: agencyBlueprint.itemTypes.map((type) => (type.key === 'change_request' ? { ...type, parents: ['requirement'] } : type)),
};

/** 交付流程多一個「暫停」（已取消類），任務流程沒有已取消類的狀態：測「沒有同類別就第一個」。 */
const withPaused: ProjectBlueprint = {
  ...agencyBlueprint,
  workflows: agencyBlueprint.workflows.map((workflow) =>
    workflow.key === 'delivery'
      ? { ...workflow, states: [...workflow.states, { key: 'paused', name: '暫停', category: 'canceled' }] }
      : workflow,
  ),
};

describe('改類型的預測：狀態', () => {
  it('新流程沒有目前的狀態：搬到第一個同類別的狀態', () => {
    // Arrange
    const item = agyItem({ state: 'internal_qa', stateCategory: 'in_progress' });

    // Act
    const preview = previewTypeChange(canMove(), item, 'task', nothingKnown);

    // Assert
    expect(preview.stateFrom?.name).toBe('內部驗收');
    expect(preview.stateTo?.key).toBe('doing');
    expect(preview.movesState).toBe(true);
    expect(stateSentence(agencyBlueprint, preview)).toBe('狀態會從「內部驗收」搬到「進行中」：「任務流程」沒有「內部驗收」。');
  });

  it('沒有同類別的狀態：搬到新流程的第一個狀態', () => {
    // Arrange
    const item = agyItem({ state: 'paused', stateCategory: 'canceled' });

    // Act
    const preview = previewTypeChange(canMove(withPaused), item, 'task', nothingKnown);

    // Assert
    expect(preview.stateTo?.key).toBe('todo');
    expect(stateSentence(withPaused, preview)).toBe('狀態會從「暫停」搬到「待辦」：「任務流程」沒有「暫停」，也沒有同類別的狀態。');
  });

  it('同一個流程（或新流程有同 key 的狀態）：狀態留著', () => {
    // Arrange
    const item = agyItem({ state: 'developing', stateCategory: 'in_progress' });

    // Act
    const preview = previewTypeChange(canMove(), item, 'change_request', nothingKnown);

    // Assert
    expect(preview.movesState).toBe(false);
    expect(preview.stateTo?.key).toBe('developing');
    expect(stateSentence(agencyBlueprint, preview)).toBe('狀態維持「開發」。');
  });

  it('新流程有限制轉換：回到起始狀態，不搬到同類別的狀態，並說明為什麼', () => {
    // Arrange
    const item = agyItem({ type: 'task', state: 'doing', stateCategory: 'in_progress' });

    // Act
    const preview = previewTypeChange(canMove(), item, 'requirement', nothingKnown);

    // Assert
    expect(preview.stateTo?.key).toBe('confirming');
    expect(preview.stateRule).toBe('restricted');
    expect(stateSentence(agencyBlueprint, preview)).toBe(
      '狀態會從「進行中」回到起始狀態：「交付流程」有限制轉換，改類型之後從起始狀態「需求確認」開始，再照一般的方式改狀態。',
    );
  });

  it('沒有「改變狀態」的權限：即使新流程不限制轉換也回到起始狀態', () => {
    // Arrange
    const item = agyItem({ state: 'internal_qa', stateCategory: 'in_progress' });
    const context: TypeChangeContext = { blueprint: agencyBlueprint, members, canTransition: false };

    // Act
    const preview = previewTypeChange(context, item, 'task', nothingKnown);

    // Assert
    expect(preview.stateTo?.key).toBe('todo');
    expect(preview.stateRule).toBe('no_permission');
    expect(stateSentence(agencyBlueprint, preview)).toBe(
      '狀態會從「內部驗收」回到起始狀態：你沒有「改變狀態」的權限，改類型之後從起始狀態「待辦」開始。',
    );
  });

  it('沒有權限、新流程又有同 key 同類別的狀態：照樣留著', () => {
    // Arrange
    const item = agyItem({ state: 'developing', stateCategory: 'in_progress' });
    const context: TypeChangeContext = { blueprint: agencyBlueprint, members, canTransition: false };

    // Act
    const preview = previewTypeChange(context, item, 'change_request', nothingKnown);

    // Assert
    expect(preview.movesState).toBe(false);
    expect(preview.stateRule).toBe('kept');
  });

  it('新流程有同 key、但類別不同的狀態：不算留著，照不限制的規則搬到同類別的狀態', () => {
    // Arrange：任務流程多一個 key 也是 closed、類別是已取消的「關閉」。
    const blueprint: ProjectBlueprint = {
      ...agencyBlueprint,
      workflows: agencyBlueprint.workflows.map((workflow) =>
        workflow.key === 'task_flow'
          ? { ...workflow, states: [...workflow.states, { key: 'closed', name: '關閉', category: 'canceled' }] }
          : workflow,
      ),
    };
    const item = agyItem({ state: 'closed', stateCategory: 'done' });

    // Act
    const preview = previewTypeChange(canMove(blueprint), item, 'task', nothingKnown);

    // Assert
    expect(preview.stateTo?.key).toBe('done');
    expect(preview.stateRule).toBe('same_category');
    expect(stateSentence(blueprint, preview)).toBe('狀態會從「結案」搬到「完成」：「任務流程」的「關閉」是另一個類別。');
  });
});

describe('改類型的預測：欄位', () => {
  it('有值、新類型沒有的自訂欄位會封存，照舊類型的順序；長文字不顯示內容', () => {
    // Arrange
    const item = agyItem({
      type: 'change_request',
      fields: { quote_amount: 1200, change_reason: '客戶要改版面', impact: 'high' },
    });

    // Act
    const preview = previewTypeChange(canMove(), item, 'task', nothingKnown);

    // Assert
    expect(preview.archived).toEqual([
      { key: 'change_reason', name: '變更原因', value: null },
      { key: 'impact', name: '影響程度', value: '高' },
      { key: 'quote_amount', name: '報價金額', value: '$1,200' },
    ]);
  });

  it('新類型也有的欄位留著，不算封存', () => {
    // Arrange
    const item = agyItem({ type: 'change_request', fields: { quote_amount: 1200, impact: 'low' } });

    // Act
    const preview = previewTypeChange(canMove(), item, 'requirement', nothingKnown);

    // Assert
    expect(preview.archived).toEqual([{ key: 'impact', name: '影響程度', value: '低' }]);
  });

  it('和伺服器同一個順序：舊類型的欄位照舊類型的順序，其他有值的照 key 排序', () => {
    // Arrange：需求沒有「影響程度」與「變更原因」，值卻在項目上（照寫入的先後是 impact 在前）。
    const item = agyItem({ fields: { impact: 'high', quote_amount: 1200, change_reason: '客戶要改版面' } });

    // Act
    const preview = previewTypeChange(canMove(), item, 'task', nothingKnown);

    // Assert
    expect(preview.archived.map((field) => field.key)).toEqual(['quote_amount', 'change_reason', 'impact']);
  });
});

describe('改類型的預測：上下層', () => {
  it('新類型不能有上層，而這個項目有上層：擋下', () => {
    // Arrange
    const item = agyItem({ type: 'task', state: 'todo', parentKey: 'AGY-3' });

    // Act
    const preview = previewTypeChange(canMove(), item, 'requirement', { parentType: undefined, children: [], hasMoreChildren: false });

    // Assert
    expect(preview.parentProblem).toEqual({ kind: 'no_parents' });
    expect(parentSentence(agencyBlueprint, item, preview)).toBe(
      '「需求」不能掛在任何類型底下，這個項目目前在 AGY-3 底下：要先拿掉上層項目才能改。',
    );
  });

  it('上層的類型不在新類型允許的上層裡：擋下並說出上層是什麼類型', () => {
    // Arrange
    const blueprint = changeUnderRequirement;
    const item = agyItem({ type: 'task', state: 'todo', parentKey: 'AGY-3' });

    // Act
    const preview = previewTypeChange(canMove(blueprint), item, 'change_request', { parentType: 'change_request', children: [], hasMoreChildren: false });

    // Assert
    expect(preview.parentProblem).toEqual({ kind: 'not_allowed', parentType: 'change_request' });
    expect(parentSentence(blueprint, item, preview)).toBe(
      '「需求變更單」不能掛在「需求變更單」底下（上層 AGY-3 是「需求變更單」）：要先換掉上層項目才能改。',
    );
  });

  it('上層的類型允許、或還不知道上層的類型時不先擋，以伺服器為準', () => {
    // Arrange
    const item = agyItem({ type: 'task', state: 'todo', parentKey: 'AGY-3' });

    // Act
    const unknown = previewTypeChange(canMove(changeUnderRequirement), item, 'change_request', { parentType: undefined, children: [], hasMoreChildren: false });
    const allowed = previewTypeChange(canMove(changeUnderRequirement), item, 'change_request', { parentType: 'requirement', children: [], hasMoreChildren: false });

    // Assert
    expect(unknown.parentProblem).toBeNull();
    expect(allowed.parentProblem).toBeNull();
    expect(parentSentence(changeUnderRequirement, item, unknown)).toBe('');
  });

  it('已經知道的下層裡，類型不能掛在新類型底下的列出來', () => {
    // Arrange
    const child = agyItem({ number: 8, type: 'task', state: 'todo', parentKey: 'AGY-1' });
    const item = agyItem({ number: 1 });

    // Act
    const toTask = previewTypeChange(canMove(), item, 'task', { parentType: null, children: [child], hasMoreChildren: false });
    const toChange = previewTypeChange(canMove(), item, 'change_request', { parentType: null, children: [child], hasMoreChildren: false });

    // Assert
    expect(toTask.blockedChildren).toEqual([child]);
    expect(toChange.blockedChildren).toEqual([]);
  });

  it('下層只載入了一部分：說明只檢查了前 N 個，以伺服器為準；全部載入時不說', () => {
    // Arrange
    const child = agyItem({ number: 8, type: 'task', state: 'todo', parentKey: 'AGY-1' });
    const item = agyItem({ number: 1 });

    // Act
    const partial = previewTypeChange(canMove(), item, 'change_request', { parentType: null, children: [child], hasMoreChildren: true });
    const complete = previewTypeChange(canMove(), item, 'change_request', { parentType: null, children: [child], hasMoreChildren: false });

    // Assert
    expect(childrenNote(partial)).toBe('只檢查了前 1 個下層，送出後以伺服器為準。');
    expect(childrenNote(complete)).toBe('');
  });
});
