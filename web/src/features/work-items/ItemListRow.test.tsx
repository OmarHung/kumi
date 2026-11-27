import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { smkDetail, smkItem } from '../../test/fixtures';
import { renderInRouter } from '../../test/renderInRouter';
import { ItemListRow } from './ItemListRow';

const TODAY = '2026-11-27';
const blueprint = smkDetail().blueprint;

function renderRow(overrides: Parameters<typeof smkItem>[0], props: { readonly projectName?: string; readonly showAssignee?: boolean } = {}) {
  renderInRouter(
    <ul>
      <ItemListRow item={smkItem({ number: 1, title: '標題', ...overrides })} blueprint={blueprint} todayDate={TODAY} {...props} />
    </ul>,
  );
  return screen.getByRole('link');
}

describe('窄螢幕的項目清單一筆', () => {
  it('過期的到期日寫「已過期 日期」並用危險色；沒過期的只有日期', () => {
    // Arrange / Act
    const link = renderRow({ dueDate: '2026-11-25' });

    // Assert
    const time = link.querySelector('time');
    expect(link).toHaveTextContent('已過期 2026/11/25');
    expect(time?.parentElement?.className).toMatch(/overdue/);
    // 看得到的是今年省掉年份的寫法，報讀照舊唸完整的日期。
    expect(time?.querySelector('[aria-hidden="true"]')).toHaveTextContent('11/25');
    expect(link).toHaveAccessibleName(/到期：已過期 2026\/11\/25/);
  });

  it('沒過期、沒有到期日的不寫「已過期」', () => {
    // Arrange / Act
    const link = renderRow({ dueDate: '2026-12-01' });

    // Assert
    expect(link).not.toHaveTextContent('已過期');
    expect(link.querySelector('time')?.parentElement?.className).not.toMatch(/overdue/);
  });

  it('標題最多兩行（有完整的 title），編號與屬性在標題的上面一行', () => {
    // Arrange / Act
    const link = renderRow({ title: '很長的標題'.repeat(20) });

    // Assert
    const title = link.querySelector('[title]');
    expect(title?.className).toMatch(/title/);
    expect(title).toHaveAttribute('title', '很長的標題'.repeat(20));
    expect(link.querySelector('[class*="main"]')).not.toBeNull();
  });

  it('只有高與緊急的優先度才出現', () => {
    // Arrange / Act
    const high = renderRow({ priority: 'high' });

    // Assert
    expect(high).toHaveAccessibleName(/優先度：高/);
  });

  it('中與低的優先度不出現；不放負責人時右欄只有到期日', () => {
    // Arrange / Act
    const link = renderRow({ priority: 'medium', dueDate: '2026-12-02' }, { showAssignee: false });

    // Assert
    expect(link).not.toHaveAccessibleName(/優先度/);
    expect(link).not.toHaveAccessibleName(/負責人/);
    expect(link).toHaveAccessibleName(/到期：2026\/12\/02/);
  });
});
