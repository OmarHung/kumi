import { render } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { avatarToneOf } from '../../components/Avatar/Avatar';
import type { SelectOption } from '../../components/Select/Select';
import { NOT_A_MEMBER } from '../../domain/fields';
import {
  AnyStateMark,
  CATEGORY_OPTIONS,
  EveryoneMark,
  NobodyMark,
  PersonMark,
  personOption,
  stateOption,
  stateOptions,
} from './selectOptionMarks';

/*
 * 下拉選項前面的小圖（docs/DESIGN.md 7.3）：表格、看板、項目頁、設定頁共用同一份。同一個清單裡每個選項給同樣大小的小圖：
 * 狀態的清單是 16px 的形狀（「所有狀態」畫「沒有值」的短線），人員的清單是 20px 的頭像（不是某一個人的畫佔位的圓）。
 */

function renderLeading(leading: ReactNode) {
  return render(<>{leading}</>).container;
}

function iconOf(option: SelectOption): string | null | undefined {
  return renderLeading(option.leading).querySelector('[data-icon]')?.getAttribute('data-icon');
}

function markOf(leading: ReactNode): string | null | undefined {
  return renderLeading(leading).querySelector('[data-mark]')?.getAttribute('data-mark');
}

describe('狀態的選項', () => {
  it('前面是狀態類別的形狀；文字預設是狀態的名稱，也可以另外給', () => {
    // Arrange
    const state = { key: 'dev', name: '開發', category: 'in_progress' as const };

    // Act
    const plain = stateOption(state);
    const current = stateOption(state, '開發（目前）');

    // Assert
    expect(plain).toMatchObject({ value: 'dev', label: '開發' });
    expect(current).toMatchObject({ value: 'dev', label: '開發（目前）' });
    expect(iconOf(plain)).toBe('stateInProgress');
  });

  it('一串狀態照原本的順序；設定頁的草稿可以換掉文字（名稱空著時用 key）', () => {
    // Arrange
    const states = [
      { key: 'todo', name: '待辦', category: 'not_started' as const },
      { key: 'new_one', name: '', category: 'done' as const },
    ];

    // Act
    const options = stateOptions(states, (state) => (state.name === '' ? state.key : state.name));

    // Assert
    expect(options.map((option) => option.label)).toEqual(['待辦', 'new_one']);
    expect(options.map(iconOf)).toEqual(['stateNotStarted', 'stateDone']);
    expect(stateOptions(states).map((option) => option.label)).toEqual(['待辦', '']);
  });

  it('類別的選項：五個，照固定的順序，前面是各自的形狀', () => {
    // Act
    const options = CATEGORY_OPTIONS;

    // Assert
    expect(options.map((option) => option.value)).toEqual(['backlog', 'not_started', 'in_progress', 'done', 'canceled']);
    expect(options.map((option) => option.label)).toEqual(['待規劃', '未開始', '進行中', '完成', '已取消']);
    expect(options.map(iconOf)).toEqual(['stateBacklog', 'stateNotStarted', 'stateInProgress', 'stateDone', 'stateCanceled']);
  });

  it('「所有狀態」不是一個狀態：「沒有值」的短線，沒有形狀', () => {
    // Act
    const container = renderLeading(<AnyStateMark />);

    // Assert
    expect(container.querySelector('[data-mark]')?.getAttribute('data-mark')).toBe('anyState');
    expect(container.querySelector('[data-icon]')).toBeNull();
  });
});

describe('人員的選項', () => {
  it('前面是頭像（名字的第一個字）；startsGroup 照給的', () => {
    // Arrange
    const person = { personId: 'p-1', displayName: '範例成員' };

    // Act
    const option = personOption(person, true);

    // Assert
    expect(option).toMatchObject({ value: 'p-1', label: '範例成員', startsGroup: true });
    expect(renderLeading(option.leading)).toHaveTextContent('範');
    expect(personOption(person).startsGroup).toBe(false);
  });

  it('名字查不到（已經不是成員）：沒有字的頭像，不畫「不」這個字', () => {
    // Act
    const container = renderLeading(<PersonMark name={NOT_A_MEMBER} />);

    // Assert
    expect(container.querySelector('[data-mark]')?.getAttribute('data-mark')).toBe('unknownPerson');
    expect(container).toHaveTextContent('');
  });

  it('沒有人是虛線的空圓、所有人是人員的圖示：都和頭像一樣是佔位的小圖', () => {
    // Act
    const nobody = markOf(<NobodyMark />);
    const everyone = renderLeading(<EveryoneMark />);

    // Assert
    expect(nobody).toBe('nobody');
    expect(everyone.querySelector('[data-mark]')?.getAttribute('data-mark')).toBe('everyone');
    expect(everyone.querySelector('[data-icon]')?.getAttribute('data-icon')).toBe('people');
  });

  it('某一個人的頭像有自己的色調（和別處的同一個人一樣）；名字查不到的、所有人、沒有人維持中性', () => {
    // Act
    const person = renderLeading(<PersonMark name="範例成員" />).querySelector('[aria-hidden="true"]');
    const placeholders = [<PersonMark key="unknown" name={NOT_A_MEMBER} />, <EveryoneMark key="everyone" />, <NobodyMark key="nobody" />].map(
      (mark) => renderLeading(mark).querySelector('[data-mark]'),
    );

    // Assert
    expect(person).toHaveAttribute('data-tone', avatarToneOf('範例成員'));
    expect(placeholders.every((mark) => mark !== null && !mark.hasAttribute('data-tone'))).toBe(true);
  });
});
