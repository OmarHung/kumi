import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { FormAlert } from '../FormAlert/FormAlert';
import { ArrivalNotice, EmptyState, ErrorState, LoadingState, RefetchErrorNotice } from './StatusViews';

describe('ErrorState', () => {
  it('第一次就讀不到：alert 裡有標題、原因與「重試」', async () => {
    // Arrange
    const user = userEvent.setup();
    const onRetry = vi.fn();
    render(<ErrorState title="專案載入失敗" error={new Error('連不上伺服器。')} onRetry={onRetry} />);

    // Act
    const alert = screen.getByRole('alert');
    await user.click(within(alert).getByRole('button', { name: '重試' }));

    // Assert
    expect(alert).toHaveTextContent('專案載入失敗');
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});

describe('RefetchErrorNotice', () => {
  it('只有背景重抓失敗時才出現，「重試」在提示旁邊', async () => {
    // Arrange
    const user = userEvent.setup();
    const refetch = vi.fn(() => Promise.resolve());
    const { rerender } = render(<RefetchErrorNotice query={{ isRefetchError: false, refetch }} message="畫面上可能是舊的。" />);
    const hiddenWhileFine = screen.queryByText('畫面上可能是舊的。');

    // Act
    rerender(<RefetchErrorNotice query={{ isRefetchError: true, refetch }} message="畫面上可能是舊的。" />);
    const message = screen.getByText('畫面上可能是舊的。');
    await user.click(within(message.parentElement as HTMLElement).getByRole('button', { name: '重試' }));

    // Assert
    expect(hiddenWhileFine).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('畫面上可能是舊的。');
    expect(refetch).toHaveBeenCalledTimes(1);
  });
});

describe('ArrivalNotice', () => {
  it('一直掛著：沒有提示時是空的，填進來還是同一個 status', () => {
    // Arrange
    const { rerender } = render(<ArrivalNotice message="" />);
    const status = screen.getByRole('status');
    const emptyText = status.textContent;

    // Act
    rerender(<ArrivalNotice message="已刪除 AGY-7「首頁」。" />);

    // Assert
    expect(emptyText).toBe('');
    expect(screen.getByRole('status')).toBe(status);
    expect(status).toHaveTextContent('已刪除 AGY-7「首頁」。');
  });
});

describe('FormAlert', () => {
  it('沒有訊息時不出現；有訊息時是 alert，給了 id 可以連到欄位', () => {
    // Arrange
    const { rerender } = render(<FormAlert message={null} />);
    const nothing = screen.queryByRole('alert');

    // Act
    rerender(
      <>
        <input aria-label="密碼" aria-describedby="login-error" />
        <FormAlert id="login-error" message="Email 或密碼不對。" />
      </>,
    );

    // Assert
    expect(nothing).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent(/^Email 或密碼不對。$/);
    expect(screen.getByRole('textbox', { name: '密碼' })).toHaveAccessibleDescription('Email 或密碼不對。');
  });
});

describe('LoadingState 與 EmptyState', () => {
  it('載入中是 status；空白說明這裡會有什麼並給下一步', () => {
    // Arrange
    render(
      <>
        <LoadingState label="正在載入設定…" />
        <EmptyState title="還沒有專案" action={<button type="button">新增專案</button>}>
          專案會列在這裡。
        </EmptyState>
      </>,
    );

    // Act
    const status = screen.getByRole('status');

    // Assert
    expect(status).toHaveTextContent('正在載入設定…');
    expect(screen.getByText('還沒有專案')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '新增專案' })).toBeInTheDocument();
  });
});
