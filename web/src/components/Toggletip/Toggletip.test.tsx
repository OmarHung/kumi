import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toggletip } from './Toggletip';

const TEXT = '成員不用設定、不用排隊';
/** 比收起來的延遲（150 毫秒）長一點。 */
const PAST_CLOSE_DELAY_MS = 300;

function renderTip() {
  const user = userEvent.setup();
  render(
    <>
      <Toggletip icon="success" label="好處" text={TEXT} />
      <button type="button">別的按鈕</button>
    </>,
  );
  return { user, trigger: screen.getByRole('button', { name: `好處：${TEXT}` }) };
}

const bubble = () => screen.queryByText(TEXT);

afterEach(() => {
  vi.useRealTimers();
});

describe('Toggletip', () => {
  it('名稱就是整句說明：報讀軟體不必打開也唸得到，浮出來的字不重複唸', () => {
    // Arrange / Act
    renderTip();

    // Assert
    expect(screen.getByRole('button', { name: `好處：${TEXT}` })).toBeInTheDocument();
    expect(bubble()).not.toBeInTheDocument();
  });

  it('滑鼠移上去出現，移開之後才收起來', () => {
    // Arrange
    vi.useFakeTimers();
    const { trigger } = renderTip();

    // Act
    fireEvent.pointerEnter(trigger, { pointerType: 'mouse' });

    // Assert
    expect(bubble()).toBeInTheDocument();
    expect(bubble()?.closest('[aria-hidden="true"]')).not.toBeNull();
    fireEvent.pointerLeave(trigger, { pointerType: 'mouse' });
    expect(bubble()).toBeInTheDocument();
    act(() => {
      vi.advanceTimersByTime(PAST_CLOSE_DELAY_MS);
    });
    expect(bubble()).not.toBeInTheDocument();
  });

  it('滑鼠從按鈕移進浮出來的框裡不會收起來（可以停在上面讀）', () => {
    // Arrange
    vi.useFakeTimers();
    const { trigger } = renderTip();
    fireEvent.pointerEnter(trigger, { pointerType: 'mouse' });

    // Act
    fireEvent.pointerLeave(trigger, { pointerType: 'mouse' });
    fireEvent.pointerEnter(bubble() as HTMLElement, { pointerType: 'mouse' });
    act(() => {
      vi.advanceTimersByTime(PAST_CLOSE_DELAY_MS);
    });

    // Assert
    expect(bubble()).toBeInTheDocument();
  });

  it('鍵盤移到按鈕上出現，移走就收起來', async () => {
    // Arrange
    const { user } = renderTip();

    // Act
    await user.tab();

    // Assert
    expect(bubble()).toBeInTheDocument();
    await user.tab();
    expect(bubble()).not.toBeInTheDocument();
  });

  it('觸控：點一下出現、再點一下收起來（觸控的移入不算）', () => {
    // Arrange
    const { trigger } = renderTip();

    // Act
    fireEvent.pointerEnter(trigger, { pointerType: 'touch' });
    expect(bubble()).not.toBeInTheDocument();
    fireEvent.click(trigger);

    // Assert
    expect(bubble()).toBeInTheDocument();
    fireEvent.click(trigger);
    expect(bubble()).not.toBeInTheDocument();
  });

  it('Esc 收起來，焦點留在按鈕上', async () => {
    // Arrange
    const { user, trigger } = renderTip();
    await user.tab();

    // Act
    await user.keyboard('{Escape}');

    // Assert
    expect(bubble()).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it('點了打開之後，點在外面收起來', () => {
    // Arrange
    const { trigger } = renderTip();
    fireEvent.click(trigger);

    // Act
    fireEvent.pointerDown(document.body);

    // Assert
    expect(bubble()).not.toBeInTheDocument();
  });
});
