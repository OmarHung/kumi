import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderInRouter } from '../../test/renderInRouter';
import { Button } from '../Button/Button';
import { List, ListRow } from './List';

describe('List', () => {
  it('是有名稱的清單，一筆一個 listitem；第一筆可以標成內容的開頭，清單下面接 footer', () => {
    // Arrange
    const ui = (
      <List label="項目" footer={<p>已載入 2 個項目</p>}>
        <ListRow to="/items/1" code="SMK-1" title="首頁設計稿" isContentStart />
        <ListRow to="/items/2" code="SMK-2" title="切版" />
      </List>
    );

    // Act
    renderInRouter(ui);

    // Assert
    const list = screen.getByRole('list', { name: '項目' });
    const rows = within(list).getAllByRole('listitem');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveAttribute('data-content-start');
    expect(rows[1]).not.toHaveAttribute('data-content-start');
    expect(list).toHaveAttribute('aria-busy', 'false');
    expect(screen.getByText('已載入 2 個項目')).toBeInTheDocument();
  });

  it('換條件時舊的結果先留著，清單標成忙碌', () => {
    // Arrange
    const ui = (
      <List label="項目" isBusy>
        <ListRow to="/items/1" title="首頁設計稿" />
      </List>
    );

    // Act
    renderInRouter(ui);

    // Assert
    expect(screen.getByRole('list', { name: '項目' })).toHaveAttribute('aria-busy', 'true');
  });

  it('有 to 時整筆是一個連結：名稱是編號、標題，再接第二行；第二行的欄位帶著只給報讀軟體的說法', () => {
    // Arrange
    const ui = (
      <List label="項目">
        <ListRow
          to="/projects/SMK/items/SMK-1"
          code="SMK-1"
          title="首頁設計稿"
          meta={[
            { key: 'state', label: '狀態', content: '待辦' },
            { key: 'assignee', content: '沒有負責人', isVisuallyHidden: true },
            { key: 'due', label: '到期', content: '2026/12/01' },
          ]}
        />
      </List>
    );

    // Act
    renderInRouter(ui);

    // Assert
    const list = screen.getByRole('list', { name: '項目' });
    const links = within(list).getAllByRole('link');
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAttribute('href', '/projects/SMK/items/SMK-1');
    expect(links[0]).toHaveAccessibleName('SMK-1 首頁設計稿 狀態：待辦 沒有負責人 到期：2026/12/01');
  });

  it('連結帶著 linkState，點了之後到那個網址', async () => {
    // Arrange
    const state = { returnTo: '/projects/SMK?view=board' };
    const { user, router } = renderInRouter(
      <List label="項目">
        <ListRow to="/projects/SMK/items/SMK-1" linkState={state} code="SMK-1" title="首頁設計稿" />
      </List>,
    );

    // Act
    await user.click(screen.getByRole('link', { name: /^SMK-1 首頁設計稿/ }));

    // Assert
    expect(router.state.location.pathname).toBe('/projects/SMK/items/SMK-1');
    expect(router.state.location.state).toEqual(state);
  });

  it('isCurrent 的那一筆連結標 aria-current（旁邊的面板開著它）；onLinkClick 可以攔下點擊，href 不變', async () => {
    // Arrange
    const clicks: string[] = [];
    const { user, router } = renderInRouter(
      <List label="下層項目">
        <ListRow to="/projects/AGY/items/AGY-5" code="AGY-5" title="首頁切版" isCurrent />
        <ListRow
          to="/projects/AGY/items/AGY-6"
          code="AGY-6"
          title="表單串接"
          onLinkClick={(event) => {
            event.preventDefault();
            clicks.push('AGY-6');
          }}
        />
      </List>,
    );
    const current = screen.getByRole('link', { name: /^AGY-5/ });
    const other = screen.getByRole('link', { name: /^AGY-6/ });

    // Act
    await user.click(other);

    // Assert
    expect(current).toHaveAttribute('aria-current', 'true');
    expect(other).not.toHaveAttribute('aria-current');
    expect(other).toHaveAttribute('href', '/projects/AGY/items/AGY-6');
    expect(clicks).toEqual(['AGY-6']);
    expect(router.state.location.pathname).toBe('/');
  });

  it('標題一行放不下時，完整的文字在 title；字串標題預設就用它自己', () => {
    // Arrange
    const ui = (
      <List label="變更集">
        <ListRow to="/a" title="很長的標題" />
        <ListRow to="/b" title={<em>沒有摘要</em>} titleText="原本的摘要" />
      </List>
    );

    // Act
    renderInRouter(ui);

    // Assert
    expect(screen.getByText('很長的標題')).toHaveAttribute('title', '很長的標題');
    expect(screen.getByText('沒有摘要').parentElement).toHaveAttribute('title', '原本的摘要');
  });

  it('沒有 to 時是靜態的一筆：不是連結，前後可以放頭像與控制項', async () => {
    // Arrange
    let removed = 0;
    const ui = (
      <List label="成員">
        <ListRow
          leading={<span aria-hidden="true">範</span>}
          title="範例成員"
          meta={[{ key: 'role', label: '角色', content: '編輯者' }]}
          trailing={
            <Button
              size="sm"
              onClick={() => {
                removed += 1;
              }}
            >
              移除
            </Button>
          }
        />
      </List>
    );
    const { user } = renderInRouter(ui);

    // Act
    const row = within(screen.getByRole('list', { name: '成員' })).getByRole('listitem');
    await user.click(within(row).getByRole('button', { name: '移除' }));

    // Assert
    expect(within(row).queryByRole('link')).not.toBeInTheDocument();
    expect(within(row).getByText('範例成員')).toBeInTheDocument();
    expect(within(row).getByText('編輯者')).toBeInTheDocument();
    expect(row).toHaveTextContent('角色：編輯者');
    expect(removed).toBe(1);
  });
});
