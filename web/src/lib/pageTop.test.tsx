import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { createRef } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { focusPageHeading, scrollToPageTop } from './pageTop';
import { useArrivalNotice } from './useArrivalNotice';

let scrollTo: MockInstance<typeof window.scrollTo>;

beforeEach(() => {
  scrollTo = vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('回到頁面的頂端', () => {
  it('scrollToPageTop 把畫面捲回最上面', () => {
    // Arrange & Act
    scrollToPageTop();

    // Assert
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });

  it('focusPageHeading 先把焦點放到標題，再捲回最上面', () => {
    // Arrange
    const heading = document.createElement('h1');
    heading.tabIndex = -1;
    document.body.append(heading);

    // Act
    focusPageHeading(heading);

    // Assert
    expect(heading).toHaveFocus();
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });

  it('沒有標題時不丟出錯誤，照樣捲回最上面', () => {
    // Arrange & Act
    focusPageHeading(null);

    // Assert
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });

  it('換頁後帶著提示到達：焦點到標題，畫面捲回最上面', () => {
    // Arrange
    const heading = document.createElement('h1');
    heading.tabIndex = -1;
    document.body.append(heading);
    const headingRef = createRef<HTMLHeadingElement>();
    headingRef.current = heading;
    const wrapper = ({ children }: { readonly children: ReactNode }) => (
      <MemoryRouter initialEntries={[{ pathname: '/projects', state: { notice: '已完成。' }, key: 'page-top-test' }]}>
        {children}
      </MemoryRouter>
    );

    // Act
    renderHook(() => useArrivalNotice(headingRef), { wrapper });

    // Assert
    expect(heading).toHaveFocus();
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });
});
