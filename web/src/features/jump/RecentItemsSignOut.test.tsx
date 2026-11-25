import { screen, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { authRequired, ok, sessionEndsAfterFirstCheck, signedInAs } from '../../test/api';
import { exampleOwner, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';
import { readRecentItems, recordRecentItem } from './recentItems';

/** 共用電腦：最近看過的項目帶著標題，下一個登入的人按 ⌘K 不能看到上一個人看過什麼。 */
describe('登出時清掉最近看過的項目', () => {
  it('從帳號選單登出：最近看過的項目清掉', async () => {
    // Arrange
    recordRecentItem({ key: 'WEB-3', projectKey: 'WEB', title: '首頁設計稿' });
    signedInAs(exampleOwner);
    server.use(http.delete('/v1/session', () => ok(null)));
    const { user } = renderApp('/my-work');
    await user.click(await screen.findByRole('button', { name: '範例使用者（範例組織 · 擁有者）' }));

    // Act
    await user.click(within(screen.getByRole('menu', { name: '帳號' })).getByRole('menuitem', { name: '登出' }));

    // Assert
    expect(await screen.findByLabelText('Email')).toBeInTheDocument();
    expect(readRecentItems()).toEqual([]);
  });

  it('工作階段失效（任何請求收到 401）導回登入頁時也清掉', async () => {
    // Arrange
    recordRecentItem({ key: 'WEB-3', projectKey: 'WEB', title: '首頁設計稿' });
    signedInAs(exampleOwner, { projects: [webProject] });
    sessionEndsAfterFirstCheck(exampleOwner);
    server.use(http.get('/v1/my-work', () => authRequired()));

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByLabelText('Email')).toBeInTheDocument();
    expect(readRecentItems()).toEqual([]);
  });
});
