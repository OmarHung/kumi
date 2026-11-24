import { afterEach, describe, expect, it, vi } from 'vitest';
import { clampWidth, createColumnWidthStore, remToPx } from './columnWidths';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('欄寬的記錄（createColumnWidthStore）', () => {
  it('換一欄的寬度：畫面上馬上換，放開（isFinal）才寫進這個瀏覽器', () => {
    // Arrange
    const store = createColumnWidthStore('example.widths');
    const listener = vi.fn();
    store.subscribe(listener);

    // Act
    store.setWidth('due', 180, false);
    const whileDragging = localStorage.getItem('example.widths');
    store.setWidth('due', 200);

    // Assert
    expect(listener).toHaveBeenCalledTimes(2);
    expect(store.getSnapshot().user).toEqual({ due: 200 });
    expect(whileDragging).toBeNull();
    expect(JSON.parse(localStorage.getItem('example.widths') ?? '{}')).toEqual({ due: 200 });
  });

  it('同一個值不通知，畫面不必重畫；快照不變（useSyncExternalStore 要同一個物件）', () => {
    // Arrange
    const store = createColumnWidthStore('example.widths');
    store.setWidth('due', 200);
    const before = store.getSnapshot();
    const listener = vi.fn();
    store.subscribe(listener);

    // Act
    store.setWidth('due', 200, false);
    store.setMeasured({});

    // Assert
    expect(listener).not.toHaveBeenCalled();
    expect(store.getSnapshot()).toBe(before);
  });

  it('saved 只跟著寫進這個瀏覽器的寬度：拖曳中的暫時值、拖曳中按 Esc 都不動它', () => {
    // Arrange
    const store = createColumnWidthStore('example.widths');
    store.setWidth('due', 200);

    // Act
    store.setWidth('due', 260, false);
    const whileDragging = store.getSnapshot().saved;
    store.setWidth('due', 200, false);
    const afterCancel = store.getSnapshot().saved;
    store.setWidth('state', 90);

    // Assert
    expect(whileDragging).toEqual({ due: 200 });
    expect(afterCancel).toEqual({ due: 200 });
    expect(store.getSnapshot().saved).toEqual({ due: 200, state: 90 });
  });

  it('照內容量出來的寬度只在記憶體：畫面上用（拉過的優先），不寫進這個瀏覽器', () => {
    // Arrange
    const store = createColumnWidthStore('example.widths');
    store.setWidth('due', 200);

    // Act
    store.setMeasured({ due: 90, state: 82 });

    // Assert
    expect(store.getSnapshot().effective).toEqual({ due: 200, state: 82 });
    expect(JSON.parse(localStorage.getItem('example.widths') ?? '{}')).toEqual({ due: 200 });
  });

  it('寬度給 undefined 是拿掉那一欄拉過的寬度（拖曳中按 Esc 回到原本沒拉過的樣子）', () => {
    // Arrange
    const store = createColumnWidthStore('example.widths');
    store.setMeasured({ due: 90 });
    store.setWidth('due', 150, false);

    // Act
    store.setWidth('due', undefined, false);

    // Assert
    expect(store.getSnapshot().user).toEqual({});
    expect(store.getSnapshot().effective).toEqual({ due: 90 });
    expect(localStorage.getItem('example.widths')).toBeNull();
  });

  it('下次打開讀得回來；重設清掉拉過的與量過的，次數加一（沒拉過的欄要重新量）', () => {
    // Arrange
    createColumnWidthStore('example.widths').setWidth('title', 320);
    const reopened = createColumnWidthStore('example.widths');
    const restored = reopened.getSnapshot().user;
    reopened.setMeasured({ due: 90 });

    // Act
    reopened.reset();

    // Assert
    expect(restored).toEqual({ title: 320 });
    expect(reopened.getSnapshot().saved).toEqual({});
    expect(reopened.getSnapshot()).toEqual({ user: {}, saved: {}, measured: {}, effective: {}, epoch: 1 });
    expect(localStorage.getItem('example.widths')).toBeNull();
  });

  it('記錄壞掉、不是數字、讀寫失敗時當成沒有設定', () => {
    // Arrange
    localStorage.setItem('broken.widths', '{not json');
    localStorage.setItem('wrong.widths', JSON.stringify({ due: 'wide', title: -5, key: 120 }));
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('quota');
    });

    // Act
    const broken = createColumnWidthStore('broken.widths');
    const wrong = createColumnWidthStore('wrong.widths');
    wrong.setWidth('due', 150);

    // Assert
    expect(broken.getSnapshot().user).toEqual({});
    // 不是正數的欄丟掉，合法的留著；存不進去照樣換畫面。
    expect(wrong.getSnapshot().user).toEqual({ key: 120, due: 150 });
  });
});

describe('寬度的範圍', () => {
  it('rem 照根元素的字級換成 px', () => {
    // Arrange / Act / Assert
    expect(remToPx(3)).toBe(48);
  });

  it('夾在最小與最大之間，取整數', () => {
    // Arrange
    const bounds = { min: 3, max: 40 };

    // Act
    const values = [clampWidth(10, bounds), clampWidth(123.6, bounds), clampWidth(900, bounds)];

    // Assert
    expect(values).toEqual([48, 124, 640]);
  });
});
