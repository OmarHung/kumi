import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { List, ListRow, type ListMeta } from '../../components/List/List';
import { agyDetail, agyItem, exampleOwner } from '../../test/fixtures';
import { renderInRouter } from '../../test/renderInRouter';
import { assigneeMeta, dueMeta, priorityMeta, projectMeta, stateMeta } from './itemListMeta';

const TODAY = '2026-11-16';
const owner = { personId: exampleOwner.personId, displayName: exampleOwner.displayName };

function renderRow(meta: readonly ListMeta[]) {
  renderInRouter(
    <List label="項目">
      <ListRow to="/projects/AGY/items/AGY-3" code="AGY-3" title="官網需求" meta={meta} />
    </List>,
  );
  return screen.getByRole('link');
}

describe('清單列的第二行', () => {
  it('每一個欄位帶著只給報讀軟體的說法：狀態、負責人、優先度、到期、專案', () => {
    // Arrange
    const item = agyItem({ number: 3, title: '官網需求', assignee: owner, priority: 'high', dueDate: '2026-11-20' });
    const detail = agyDetail();

    // Act
    const link = renderRow([
      stateMeta(item, detail.blueprint),
      assigneeMeta(item),
      ...priorityMeta(item),
      ...dueMeta(item, TODAY),
      projectMeta(item, detail),
    ]);

    // Assert
    expect(link).toHaveAccessibleName('AGY-3 官網需求 狀態：需求確認 負責人：範例使用者 優先度：高 到期：2026/11/20 專案：接案測試');
  });

  it('沒有負責人時只唸「沒有負責人」；沒有設優先度與到期日時不佔位置', () => {
    // Arrange
    const item = agyItem({ number: 3, title: '官網需求' });

    // Act
    const link = renderRow([stateMeta(item, undefined), assigneeMeta(item), ...priorityMeta(item), ...dueMeta(item, TODAY)]);

    // Assert
    expect(priorityMeta(item)).toEqual([]);
    expect(dueMeta(item, TODAY)).toEqual([]);
    expect(link).toHaveAccessibleName('AGY-3 官網需求 狀態：未開始 沒有負責人');
  });

  it('專案內容還沒讀到時用專案代號', () => {
    // Arrange
    const item = agyItem({ number: 3 });

    // Act
    render(<p>{projectMeta(item, undefined).content}</p>);

    // Assert
    expect(screen.getByText('AGY')).toBeInTheDocument();
  });
});
