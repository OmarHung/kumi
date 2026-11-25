import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { authRequired, ok, sessionEndsAfterFirstCheck, signedInAs } from '../../test/api';
import { exampleOwner, webProject } from '../../test/fixtures';
import { renderApp } from '../../test/renderApp';
import { server } from '../../test/server';

/** 共用電腦：上一個人打到一半的留言與說明，登出之後不能留在這個分頁裡。 */
function leaveDrafts() {
  sessionStorage.setItem('draft.comment.SMK-7', '寫到一半的留言');
  sessionStorage.setItem('draft.description.AGY-1', '寫到一半的說明');
  sessionStorage.setItem('other.key', '不是草稿');
}

function draftKeys(): string[] {
  return Object.keys(sessionStorage).filter((key) => key.startsWith('draft.'));
}

describe('登出時清掉草稿', () => {
  it('從帳號選單登出：所有 draft.* 都清掉，別的照樣留著', async () => {
    // Arrange
    leaveDrafts();
    signedInAs(exampleOwner);
    server.use(http.delete('/v1/session', () => ok(null)));
    const { user } = renderApp('/my-work');
    await user.click(await screen.findByRole('button', { name: '範例使用者（範例組織 · 擁有者）' }));

    // Act
    await user.click(within(screen.getByRole('menu', { name: '帳號' })).getByRole('menuitem', { name: '登出' }));

    // Assert
    expect(await screen.findByLabelText('Email')).toBeInTheDocument();
    expect(draftKeys()).toEqual([]);
    expect(sessionStorage.getItem('other.key')).toBe('不是草稿');
  });

  it('登出失敗時草稿留著（還沒登出，畫面不動）', async () => {
    // Arrange
    leaveDrafts();
    signedInAs(exampleOwner);
    server.use(http.delete('/v1/session', () => new Response(null, { status: 500 })));
    const { user } = renderApp('/my-work');
    await user.click(await screen.findByRole('button', { name: '範例使用者（範例組織 · 擁有者）' }));

    // Act
    await user.click(within(screen.getByRole('menu', { name: '帳號' })).getByRole('menuitem', { name: '登出' }));

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('登出失敗');
    expect(draftKeys()).toHaveLength(2);
  });

  it('工作階段失效（任何請求收到 401）導回登入頁時也清掉', async () => {
    // Arrange
    leaveDrafts();
    signedInAs(exampleOwner, { projects: [webProject] });
    sessionEndsAfterFirstCheck(exampleOwner);
    server.use(http.get('/v1/my-work', () => authRequired()));

    // Act
    renderApp('/my-work');

    // Assert
    expect(await screen.findByLabelText('Email')).toBeInTheDocument();
    await waitFor(() => {
      expect(draftKeys()).toEqual([]);
    });
  });
});
