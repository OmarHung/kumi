import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createRef } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { Notice } from './Notice';

describe('Notice', () => {
  it('錯誤預設是 alert，其他是 status', () => {
    // Arrange
    render(
      <>
        <Notice tone="danger">儲存失敗。</Notice>
        <Notice tone="warning">畫面可能是舊的。</Notice>
      </>,
    );

    // Act
    const alert = screen.getByRole('alert');
    const status = screen.getByRole('status');

    // Assert
    expect(alert).toHaveTextContent('儲存失敗。');
    expect(status).toHaveTextContent('畫面可能是舊的。');
  });

  it('可以指定 role；none 是不會自己唸的說明', () => {
    // Arrange
    render(
      <>
        <Notice tone="info" role="alert">
          先選一個流程。
        </Notice>
        <Notice tone="info" role="none">
          只有勾起來的轉換可以走。
        </Notice>
      </>,
    );

    // Act
    const alerts = screen.getAllByRole('alert');

    // Assert
    expect(alerts).toHaveLength(1);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    expect(screen.getByText('只有勾起來的轉換可以走。')).toBeInTheDocument();
  });

  it('標題、內容與動作都在提示裡', () => {
    // Arrange
    render(
      <Notice tone="warning" title="別人已經套用了新版本" actions={<button type="button">以最新版本重新開始</button>}>
        設定已經更新為第 5 版。
      </Notice>,
    );

    // Act
    const status = screen.getByRole('status');

    // Assert
    expect(status).toHaveTextContent('別人已經套用了新版本設定已經更新為第 5 版。以最新版本重新開始');
    expect(within(status).getByRole('button', { name: '以最新版本重新開始' })).toBeInTheDocument();
  });

  it('有 onDismiss 才有「知道了」，名稱可以帶上對象', async () => {
    // Arrange
    const user = userEvent.setup();
    const onDismiss = vi.fn();
    render(
      <>
        <Notice tone="danger" onDismiss={onDismiss} dismissLabel="知道了（AGY-4）">
          AGY-4 沒有移過去。
        </Notice>
        <Notice tone="success">已套用。</Notice>
      </>,
    );

    // Act
    await user.click(screen.getByRole('button', { name: '知道了（AGY-4）' }));

    // Assert
    expect(onDismiss).toHaveBeenCalledTimes(1);
    expect(within(screen.getByRole('status')).queryByRole('button')).not.toBeInTheDocument();
  });

  it('沒有內容時元素先掛著（空的），之後填進來還是同一個元素', () => {
    // Arrange
    const { rerender } = render(
      <Notice tone="success" role="status">
        {''}
      </Notice>,
    );
    const empty = screen.getByRole('status');
    const emptyText = empty.textContent;

    // Act
    rerender(
      <Notice tone="success" role="status">
        已刪除 AGY-7「首頁」。
      </Notice>,
    );

    // Assert
    expect(emptyText).toBe('');
    expect(screen.getByRole('status')).toBe(empty);
    expect(empty).toHaveTextContent('已刪除 AGY-7「首頁」。');
  });

  it('內容可以是段落與清單，照樣在提示裡', () => {
    // Arrange
    render(
      <Notice tone="danger" title="還有 2 個地方要修正">
        <p>「開發」沒有出路。</p>
        <ul>
          <li>「交付流程」要先填「客戶驗收日」。</li>
        </ul>
      </Notice>,
    );

    // Act
    const alert = screen.getByRole('alert');

    // Assert
    expect(within(alert).getByText('「開發」沒有出路。')).toBeInTheDocument();
    expect(within(alert).getByRole('listitem')).toHaveTextContent('「交付流程」要先填「客戶驗收日」。');
  });

  it('給了 id 就能讓欄位用 aria-describedby 連過來', () => {
    // Arrange
    render(
      <>
        <input aria-label="Email" aria-describedby="login-error" />
        <Notice id="login-error" tone="danger">
          Email 或密碼不對。
        </Notice>
      </>,
    );

    // Act
    const input = screen.getByRole('textbox', { name: 'Email' });

    // Assert
    expect(input).toHaveAccessibleDescription('Email 或密碼不對。');
  });

  it('收 ref 與 tabIndex：按的按鈕不見之後，程式可以把焦點放到提示本身（Tab 不停在它上面）', () => {
    // Arrange
    const ref = createRef<HTMLDivElement>();
    render(
      <Notice ref={ref} tabIndex={-1} tone="warning" role="alert">
        別人已經套用了新版本。
      </Notice>,
    );

    // Act
    act(() => {
      ref.current?.focus();
    });

    // Assert
    const alert = screen.getByRole('alert');
    expect(alert).toBe(ref.current);
    expect(alert).toHaveFocus();
    expect(alert).toHaveAttribute('tabindex', '-1');
    expect(alert).toHaveTextContent('別人已經套用了新版本。');
  });
});
