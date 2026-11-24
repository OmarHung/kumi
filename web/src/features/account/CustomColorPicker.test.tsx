import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { hexToHsv, hsvToHex } from '../../lib/hexColor';
import { CustomColorPicker } from './CustomColorPicker';

/** 和真的用法一樣：選的顏色存在外面，改了再傳回來。 */
function Harness({ initial, onChange }: { readonly initial: string; readonly onChange: (hex: string) => void }) {
  const [value, setValue] = useState(initial);
  return (
    <CustomColorPicker
      value={value}
      onChange={(hex) => {
        setValue(hex);
        onChange(hex);
      }}
    />
  );
}

function setup(initial: string) {
  const onChange = vi.fn<(hex: string) => void>();
  const user = userEvent.setup();
  render(<Harness initial={initial} onChange={onChange} />);
  return {
    user,
    onChange,
    area: screen.getByRole('slider', { name: '飽和度與亮度' }),
    hue: screen.getByRole('slider', { name: '色相' }),
    field: screen.getByRole('textbox', { name: '色碼' }),
  };
}

describe('CustomColorPicker', () => {
  it('色碼欄位與兩個滑桿都是目前的顏色', () => {
    // Arrange & Act
    const { area, hue, field } = setup('#3366cc');

    // Assert
    expect(field).toHaveValue('#3366cc');
    expect(hue).toHaveAttribute('aria-valuenow', '220');
    expect(area).toHaveAttribute('aria-valuetext', '飽和度 75%、亮度 80%，#3366cc');
  });

  it('打完六碼就換，不用按 Enter；滑桿跟著換；打到一半經過的三碼（#00f）不算', async () => {
    // Arrange
    const { user, onChange, hue, field } = setup('#e5322b');

    // Act
    await user.clear(field);
    await user.type(field, '#00ff00');

    // Assert
    expect(onChange.mock.calls).toEqual([['#00ff00']]);
    expect(hue).toHaveAttribute('aria-valuenow', '120');
  });

  it('三碼的簡寫按 Enter 才換；一個字一個字刪的時候經過三碼不會換顏色', async () => {
    // Arrange
    const { user, onChange, field } = setup('#e5322b');
    await user.click(field);
    // #e5322b 刪三個字是 #e53（合格的三碼簡寫）。
    await user.keyboard('{End}{Backspace}{Backspace}{Backspace}');
    const whileDeleting = onChange.mock.calls.length;

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(field).toHaveValue('#ee5533');
    expect(whileDeleting).toBe(0);
    expect(onChange.mock.calls).toEqual([['#ee5533']]);
  });

  it('三碼的簡寫離開欄位時也換', async () => {
    // Arrange
    const { user, onChange, field } = setup('#e5322b');
    await user.clear(field);
    await user.type(field, 'abc');

    // Act
    await user.tab();

    // Assert
    expect(onChange.mock.calls).toEqual([['#aabbcc']]);
    expect(field).toHaveValue('#aabbcc');
  });

  it('按 Enter 時色碼不合格式，馬上說明', async () => {
    // Arrange
    const { user, onChange, field } = setup('#e5322b');
    await user.clear(field);
    await user.type(field, '#12');

    // Act
    await user.keyboard('{Enter}');

    // Assert
    expect(field).toHaveAttribute('aria-invalid', 'true');
    expect(field).toHaveFocus();
    expect(onChange).not.toHaveBeenCalled();
  });

  it('色碼不合格式時不換顏色，離開欄位才說明', async () => {
    // Arrange
    const { user, onChange, field } = setup('#e5322b');

    // Act
    await user.clear(field);
    await user.type(field, '#12');
    const whileTyping = field.getAttribute('aria-invalid');
    await user.tab();

    // Assert
    expect(onChange).not.toHaveBeenCalled();
    expect(whileTyping).not.toBe('true');
    expect(field).toHaveAttribute('aria-invalid', 'true');
    expect(field).toHaveAccessibleDescription('請輸入像 #1a2b3c 這樣的色碼。');
  });

  it('飽和度與亮度用方向鍵調：上下是亮度、左右是飽和度，按著 Shift 一次 10%', async () => {
    // Arrange
    const { user, onChange, area } = setup('#ff0000');
    area.focus();

    // Act
    await user.keyboard('{ArrowDown}');
    const afterDown = onChange.mock.lastCall?.[0];
    await user.keyboard('{Shift>}{ArrowLeft}{/Shift}');

    // Assert
    expect(afterDown).toBe(hsvToHex({ h: 0, s: 1, v: 0.99 }));
    expect(onChange).toHaveBeenLastCalledWith(hsvToHex({ h: 0, s: 0.9, v: 0.99 }));
    expect(area).toHaveAttribute('aria-valuetext', `飽和度 90%、亮度 99%，${hsvToHex({ h: 0, s: 0.9, v: 0.99 })}`);
  });

  it('色相用方向鍵調，Home、End 到兩端', async () => {
    // Arrange
    const { user, onChange, hue } = setup('#ff0000');
    hue.focus();

    // Act
    await user.keyboard('{ArrowRight}');
    const afterRight = hue.getAttribute('aria-valuenow');
    await user.keyboard('{End}');
    const atEnd = hue.getAttribute('aria-valuenow');
    await user.keyboard('{Home}');

    // Assert
    expect(afterRight).toBe('1');
    expect(atEnd).toBe('359');
    expect(hue).toHaveAttribute('aria-valuenow', '0');
    expect(onChange).toHaveBeenLastCalledWith('#ff0000');
  });

  it('調到全黑再調亮，色相不會跑掉', async () => {
    // Arrange
    const { user, onChange, area } = setup('#3366cc');
    area.focus();

    // Act
    for (let step = 0; step < 10; step += 1) await user.keyboard('{Shift>}{ArrowDown}{/Shift}');
    const black = onChange.mock.lastCall?.[0];
    await user.keyboard('{Shift>}{ArrowUp}{/Shift}');

    // Assert
    expect(black).toBe('#000000');
    expect(hexToHsv(onChange.mock.lastCall?.[0] ?? '').h).toBeCloseTo(220, -1);
  });

  it('在區塊上按下就選到那一點：左右是飽和度、上下是亮度', () => {
    // Arrange
    const { onChange, area } = setup('#ff0000');
    vi.spyOn(area, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 0, y: 0, width: 200, height: 100 }));

    // Act
    fireEvent.pointerDown(area, { pointerId: 1, button: 0, clientX: 100, clientY: 25 });

    // Assert
    expect(onChange).toHaveBeenLastCalledWith(hsvToHex({ h: 0, s: 0.5, v: 0.75 }));
  });

  it('在色相的軌道上按下就選到那一個色相', () => {
    // Arrange
    const { onChange, hue } = setup('#ff0000');
    vi.spyOn(hue, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 0, y: 0, width: 360, height: 24 }));

    // Act
    fireEvent.pointerDown(hue, { pointerId: 1, button: 0, clientX: 120, clientY: 12 });

    // Assert
    expect(onChange).toHaveBeenLastCalledWith('#00ff00');
    expect(hue).toHaveAttribute('aria-valuenow', '120');
  });
  it('按著拖曳一路跟著；放開之後移動不再改', () => {
    // Arrange
    const { onChange, area } = setup('#ff0000');
    vi.spyOn(area, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 0, y: 0, width: 200, height: 100 }));
    fireEvent.pointerDown(area, { pointerId: 1, button: 0, clientX: 0, clientY: 0 });

    // Act
    fireEvent.pointerMove(area, { pointerId: 1, clientX: 200, clientY: 50 });
    const whileDragging = onChange.mock.lastCall?.[0];
    fireEvent.pointerUp(area, { pointerId: 1, clientX: 200, clientY: 50 });
    const callsAfterRelease = onChange.mock.calls.length;
    fireEvent.pointerMove(area, { pointerId: 1, clientX: 100, clientY: 100 });

    // Assert
    expect(whileDragging).toBe(hsvToHex({ h: 0, s: 1, v: 0.5 }));
    expect(onChange).toHaveBeenCalledTimes(callsAfterRelease);
  });

  it('右鍵按下不選顏色', () => {
    // Arrange
    const { onChange, area } = setup('#ff0000');
    vi.spyOn(area, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 0, y: 0, width: 200, height: 100 }));

    // Act
    fireEvent.pointerDown(area, { pointerId: 1, button: 2, clientX: 100, clientY: 50 });
    fireEvent.pointerMove(area, { pointerId: 1, clientX: 150, clientY: 50 });

    // Assert
    expect(onChange).not.toHaveBeenCalled();
  });

  it('色碼沒有變時不送出（拖曳中一秒會動很多次）', () => {
    // Arrange：灰色沒有色相，拖色相的軌道不會改變色碼。
    const { onChange, hue } = setup('#808080');
    vi.spyOn(hue, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 0, y: 0, width: 360, height: 24 }));

    // Act
    fireEvent.pointerDown(hue, { pointerId: 1, button: 0, clientX: 200, clientY: 12 });

    // Assert
    expect(onChange).not.toHaveBeenCalled();
    expect(hue).toHaveAttribute('aria-valuenow', '200');
  });

  it('色碼的錯誤在碰了滑桿之後清掉，欄位換成滑桿選的顏色', async () => {
    // Arrange
    const { user, field, hue } = setup('#ff0000');
    await user.clear(field);
    await user.type(field, '#12');
    await user.tab();

    // Act
    hue.focus();
    await user.keyboard('{End}');

    // Assert
    expect(field).not.toHaveAttribute('aria-invalid', 'true');
    expect(field).toHaveValue(hsvToHex({ h: 359, s: 1, v: 1 }));
  });

  it('色相是小數時，方向鍵照畫面上的度數加減', async () => {
    // Arrange：#ff0002 的色相大約是 359.5°，畫面上是 359。
    const { user, onChange, hue } = setup('#ff0002');
    hue.focus();

    // Act
    await user.keyboard('{ArrowLeft}');

    // Assert
    expect(hue).toHaveAttribute('aria-valuenow', '358');
    expect(hexToHsv(onChange.mock.lastCall?.[0] ?? '').h).toBeCloseTo(358, 0);
  });

  it('外面傳進來的顏色變了（別的分頁改了），滑桿與欄位跟著換', () => {
    // Arrange
    const onChange = vi.fn<(hex: string) => void>();
    const { rerender } = render(<CustomColorPicker value="#ff0000" onChange={onChange} />);

    // Act
    rerender(<CustomColorPicker value="#00ff00" onChange={onChange} />);

    // Assert
    expect(screen.getByRole('slider', { name: '色相' })).toHaveAttribute('aria-valuenow', '120');
    expect(screen.getByRole('textbox', { name: '色碼' })).toHaveValue('#00ff00');
  });
});
