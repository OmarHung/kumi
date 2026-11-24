import { act } from '@testing-library/react';
import { vi } from 'vitest';

/**
 * jsdom 沒有 IntersectionObserver：換成假的，report(false) 模擬「看的元素被捲出看得到的範圍」。
 * 記得在 afterEach 呼叫 vi.unstubAllGlobals()。
 */
export function stubIntersectionObserver() {
  const callbacks = new Set<IntersectionObserverCallback>();
  class FakeIntersectionObserver {
    readonly root = null;
    readonly rootMargin = '';
    readonly thresholds = [0];
    private readonly callback: IntersectionObserverCallback;

    constructor(callback: IntersectionObserverCallback) {
      this.callback = callback;
    }

    observe() {
      callbacks.add(this.callback);
    }

    disconnect() {
      callbacks.delete(this.callback);
    }

    unobserve() {
      callbacks.delete(this.callback);
    }

    takeRecords(): IntersectionObserverEntry[] {
      return [];
    }
  }
  vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver);
  return {
    get observing() {
      return callbacks.size;
    },
    report(isIntersecting: boolean) {
      const entry = { isIntersecting } as IntersectionObserverEntry;
      act(() => {
        callbacks.forEach((callback) => {
          callback([entry], {} as IntersectionObserver);
        });
      });
    },
  };
}
