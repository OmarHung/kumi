import { act, render, screen, waitFor } from '@testing-library/react';
import { useRef } from 'react';
import { createMemoryRouter, Link } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { describe, expect, it } from 'vitest';
import { useArrivalNotice } from './useArrivalNotice';

function Page() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const notice = useArrivalNotice(headingRef);
  return (
    <>
      <h1 ref={headingRef} tabIndex={-1}>
        專案
      </h1>
      <p role="status">{notice}</p>
      <Link to="/projects?view=board">看板</Link>
      <Link to="/elsewhere">別的頁</Link>
    </>
  );
}

function Elsewhere() {
  return <Link to="/projects">回去</Link>;
}

/** 從別的頁帶著提示換到專案頁（和刪除項目、把自己移出專案之後一樣）。 */
async function arriveWithNotice() {
  const router = createMemoryRouter(
    [
      { path: '/projects', element: <Page /> },
      { path: '/elsewhere', element: <Elsewhere /> },
    ],
    { initialEntries: ['/elsewhere'] },
  );
  render(<RouterProvider router={router} />);
  await act(async () => {
    await router.navigate('/projects', { state: { notice: '已刪除 AGY-7「首頁」。' } });
  });
  // 提示的區塊先掛上、文字晚一點才填（報讀軟體才唸得到），負載高時 findByRole 可能拿到還空著的它：等到文字出現。
  await waitFor(() => {
    expect(screen.getByRole('status')).toHaveTextContent('已刪除 AGY-7「首頁」。');
  });
  expect(screen.getByRole('heading', { name: '專案' })).toHaveFocus();
  return router;
}

describe('換頁之後的提示', () => {
  it('換到沒有提示的網址（例如切換檢視）時清掉，不會一直掛著', async () => {
    // Arrange
    const router = await arriveWithNotice();

    // Act
    await act(async () => {
      await router.navigate('/projects?view=board');
    });

    // Assert
    // 清掉提示是下一個 tick 才發生的，負載高時同步看會輸給它。
    await waitFor(() => {
      expect(screen.getByRole('status')).toBeEmptyDOMElement();
    });
  });

  it('按上一頁回到帶著提示的那一頁：提示不再出現，焦點也不再被拉到標題', async () => {
    // Arrange
    const router = await arriveWithNotice();
    await act(async () => {
      await router.navigate('/elsewhere');
    });
    screen.getByRole('link', { name: '回去' }).focus();

    // Act
    await act(async () => {
      await router.navigate(-1);
    });

    // Assert
    expect(await screen.findByRole('heading', { name: '專案' })).not.toHaveFocus();
    expect(screen.getByRole('status')).toHaveTextContent('');
  });

  it('同一頁裡換了網址再按上一頁回來：提示不再出現', async () => {
    // Arrange
    const router = await arriveWithNotice();
    await act(async () => {
      await router.navigate('/projects?view=board');
    });
    screen.getByRole('link', { name: '看板' }).focus();

    // Act
    await act(async () => {
      await router.navigate(-1);
    });

    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('');
    expect(screen.getByRole('link', { name: '看板' })).toHaveFocus();
  });
});
