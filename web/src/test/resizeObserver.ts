import { act } from '@testing-library/react';
import { vi } from 'vitest';

/**
 * jsdom 沒有 ResizeObserver，也量不出寬度：換成假的，report 讓符合選擇器的元素「變成」某個寬度。
 * 記得在 afterEach 呼叫 vi.unstubAllGlobals()。
 */
export function stubResizeObserver() {
  const observers = new Set<{ readonly callback: ResizeObserverCallback; readonly targets: Set<Element> }>();
  class FakeResizeObserver {
    private readonly entry: { readonly callback: ResizeObserverCallback; readonly targets: Set<Element> };

    constructor(callback: ResizeObserverCallback) {
      this.entry = { callback, targets: new Set() };
      observers.add(this.entry);
    }

    observe(target: Element) {
      this.entry.targets.add(target);
    }

    unobserve(target: Element) {
      this.entry.targets.delete(target);
    }

    disconnect() {
      observers.delete(this.entry);
    }
  }
  vi.stubGlobal('ResizeObserver', FakeResizeObserver);
  return {
    report(selector: string, width: number) {
      act(() => {
        for (const { callback, targets } of observers) {
          const matched = [...targets].filter((target) => target.matches(selector));
          if (matched.length === 0) continue;
          const entries = matched.map((target) => ({ target, contentRect: { width, height: 0 } }) as unknown as ResizeObserverEntry);
          callback(entries, {} as ResizeObserver);
        }
      });
    },
  };
}
