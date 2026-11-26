import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';

describe('項目頁的建立者', () => {
  it('欄位區顯示建立的人，唯讀：不是按鈕，也不是負責人', async () => {
    // Arrange
    const item = detailOf(
      smkItem({ number: 7, title: '首頁設計稿', assignee: null, createdBy: { personId: 'person-example-2', displayName: '建立的人' } }),
      '說明',
    );
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(item);
    serveActivity(item.key, []);

    // Act
    renderApp('/projects/SMK/items/SMK-7');

    // Assert
    const fields = await screen.findByRole('region', { name: '欄位' });
    const label = await within(fields).findByText('建立者');
    expect(label.nextElementSibling).toHaveTextContent('建立的人');
    expect(within(fields).queryByRole('button', { name: /建立者/ })).not.toBeInTheDocument();
    expect(within(fields).getByRole('button', { name: '負責人：未填' })).toBeInTheDocument();
  });
});
