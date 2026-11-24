import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Activity } from '../../api/types';
import { commented, created, deletedComment, manyComments, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import type * as CommentTarget from './commentTarget';
import { commentHash } from './commentTarget';

// 計時器照實際的 2.4 秒會讓測試太慢：只縮短這一個常數，其他照正式的邏輯。
vi.mock('./commentTarget', async (importOriginal) => ({
  ...(await importOriginal<typeof CommentTarget>()),
  HIGHLIGHT_MS: 150,
}));

const scrollIntoView = vi.fn();

beforeEach(() => {
  // jsdom 沒有 scrollIntoView：換成記錄呼叫的假函式
  Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', { configurable: true, value: scrollIntoView });
});

afterEach(() => {
  scrollIntoView.mockReset();
  Reflect.deleteProperty(HTMLElement.prototype, 'scrollIntoView');
  vi.unstubAllGlobals();
});

function serveProject() {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  const items = [smkItem({ number: 7, title: '首頁設計稿' }), smkItem({ number: 8, title: '登入流程' })];
  serveItems('SMK', items);
  items.forEach((item) => {
    serveItem(detailOf(item));
  });
}

function highlighted(): HTMLElement | null {
  return document.querySelector<HTMLElement>('[data-highlighted]');
}

function entryWithText(text: string): HTMLElement {
  const entry = screen.getByText(text).closest('li');
  if (entry === null) throw new Error(`找不到含「${text}」的那一筆`);
  return entry;
}

describe('時間軸：捲到網址指的那一則留言並閃一下', () => {
  it('網址指到已刪除的留言：照樣捲過去、閃一下佔位', async () => {
    // Arrange
    const target = deletedComment();
    serveProject();
    serveActivity('SMK-7', [created(), target, commented('後面的')]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);

    // Assert
    await screen.findByText('這則留言已刪除。');
    await waitFor(() => {
      expect(highlighted()).toBe(entryWithText('這則留言已刪除。'));
    });
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
  });

  it('找到那一筆：捲到畫面中間、標上 data-highlighted（a 或 b），約一下子之後拿掉', async () => {
    // Arrange
    const target = commented('要看的這一則');
    serveProject();
    serveActivity('SMK-7', [created(), target, commented('後面的')]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);

    // Assert
    await screen.findByText('要看的這一則');
    await waitFor(() => {
      expect(highlighted()).toBe(entryWithText('要看的這一則'));
    });
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center', behavior: 'smooth' });
    expect(scrollIntoView.mock.contexts[0]).toBe(entryWithText('要看的這一則'));
    await waitFor(() => {
      expect(highlighted()).toBeNull();
    });
  });

  it('使用者要求減少動態效果：捲動不用平滑', async () => {
    // Arrange
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === '(prefers-reduced-motion: reduce)',
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));
    const target = commented('要看的這一則');
    serveProject();
    serveActivity('SMK-7', [created(), target]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);

    // Assert
    await waitFor(() => {
      expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center', behavior: 'auto' });
    });
  });

  it('目標是回覆：捲到那一則回覆並只標那一則（回覆它的那一串不一起亮），報讀「已捲到 … 的回覆」', async () => {
    // Arrange
    const top = commented('最上層');
    const reply = commented('要看的回覆', { replyToId: top.id });
    serveProject();
    serveActivity('SMK-7', [created(), top, reply]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash(reply.id)}`);

    // Assert
    await waitFor(() => {
      expect(highlighted()).toBe(entryWithText('要看的回覆'));
    });
    expect(entryWithText('最上層')).not.toHaveAttribute('data-highlighted');
    expect(scrollIntoView.mock.contexts[0]).toBe(entryWithText('要看的回覆'));
    expect(await screen.findByText('已捲到 範例使用者 的回覆。')).toBeInTheDocument();
  });

  it('沒有 hash、或 hash 不是留言的格式：不捲、不閃', async () => {
    // Arrange
    serveProject();
    serveActivity('SMK-7', [created(), commented('一則')]);

    // Act
    renderApp('/projects/SMK/items/SMK-7#other-1');

    // Assert
    await screen.findByText('一則');
    expect(scrollIntoView).not.toHaveBeenCalled();
    expect(highlighted()).toBeNull();
  });

  it('目標在第二頁：自動載入下一頁，找到之後才捲過去', async () => {
    // Arrange
    const history: Activity[] = [created(), ...manyComments(60)];
    const target = history[60] as Activity;
    serveProject();
    const activity = serveActivity('SMK-7', history);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);

    // Assert
    await waitFor(() => {
      expect(highlighted()).toBe(entryWithText('留言 60'));
    });
    expect(activity.requests).toHaveLength(2);
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
  });

  it('目標不在任何一頁：載完為止，什麼都不做也不報錯', async () => {
    // Arrange
    serveProject();
    const activity = serveActivity('SMK-7', [created(), ...manyComments(60)]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash('0190c000-0000-7000-8000-00000000ffff')}`);

    // Assert
    await waitFor(() => {
      expect(activity.requests).toHaveLength(2);
    });
    await screen.findByText('留言 60');
    expect(scrollIntoView).not.toHaveBeenCalled();
    expect(highlighted()).toBeNull();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('已經處理過的這一次導航：新留言進來、重畫都不再捲', async () => {
    // Arrange
    const target = commented('要看的這一則');
    serveProject();
    const timeline = serveActivity('SMK-7', [created(), target]);
    const { queryClient } = renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);
    await waitFor(() => {
      expect(highlighted()).not.toBeNull();
    });
    await waitFor(() => {
      expect(highlighted()).toBeNull();
    });

    // Act
    timeline.add(commented('新來的'));
    await queryClient.invalidateQueries({ queryKey: ['item', 'SMK-7', 'activity'] });

    // Assert
    await screen.findByText('新來的');
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
    expect(highlighted()).toBeNull();
  });
});

describe('時間軸：閃的細節', () => {
  it('StrictMode（effect 掛上、清理、再掛上）：標出來之後照樣會拿掉', async () => {
    // Arrange
    const target = commented('要看的這一則');
    serveProject();
    serveActivity('SMK-7', [created(), target]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`, { strict: true });

    // Assert
    await waitFor(() => {
      expect(highlighted()).not.toBeNull();
    });
    await waitFor(() => {
      expect(highlighted()).toBeNull();
    });
  });

  it('捲到之後說一句給報讀軟體：已捲到某人的留言（和「新活動」的 status 分開）', async () => {
    // Arrange
    const target = commented('要看的這一則');
    serveProject();
    serveActivity('SMK-7', [created(), target]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);

    // Assert
    const status = await screen.findByText(`已捲到 ${target.actor.displayName} 的留言。`);
    expect(status).toHaveAttribute('role', 'status');
    expect(status).toHaveClass('visually-hidden');
    expect(screen.getAllByRole('status').length).toBeGreaterThanOrEqual(2);
  });

  it('留言比可見的高度還高：對齊開頭（start），不然開頭被切掉', async () => {
    // Arrange
    Object.defineProperty(HTMLElement.prototype, 'offsetHeight', { configurable: true, value: 5000 });
    const target = commented('很長的留言');
    serveProject();
    serveActivity('SMK-7', [created(), target]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);

    // Assert
    try {
      await waitFor(() => {
        expect(scrollIntoView).toHaveBeenCalledWith({ block: 'start', behavior: 'smooth' });
      });
    } finally {
      Reflect.deleteProperty(HTMLElement.prototype, 'offsetHeight');
    }
  });
});

describe('時間軸：再點一次同一則', () => {
  it('2.4 秒內再點同一則：data-highlighted 換成另一個值，動畫才會重新開始', async () => {
    // Arrange
    const target = commented('要看的這一則');
    serveProject();
    serveActivity('SMK-7', [created(), target]);
    const { router } = renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);
    await waitFor(() => {
      expect(highlighted()).not.toBeNull();
    });
    const first = highlighted()?.dataset.highlighted;

    // Act
    await router.navigate(`/projects/SMK/items/SMK-7${commentHash(target.id)}`, { replace: true });

    // Assert
    await waitFor(() => {
      expect(highlighted()?.dataset.highlighted).not.toBe(first);
    });
    expect(scrollIntoView).toHaveBeenCalledTimes(2);
  });

  it('新的導航（replace 換了 key）會再捲一次、再閃一次', async () => {
    // Arrange
    const target = commented('要看的這一則');
    serveProject();
    serveActivity('SMK-7', [created(), target]);
    const { router } = renderApp(`/projects/SMK/items/SMK-7${commentHash(target.id)}`);
    await waitFor(() => {
      expect(highlighted()).not.toBeNull();
    });
    await waitFor(() => {
      expect(highlighted()).toBeNull();
    });

    // Act
    await router.navigate(`/projects/SMK/items/SMK-7${commentHash(target.id)}`, { replace: true });

    // Assert
    await waitFor(() => {
      expect(highlighted()).toBe(entryWithText('要看的這一則'));
    });
    expect(scrollIntoView).toHaveBeenCalledTimes(2);
  });
});

describe('時間軸：項目面板', () => {
  it('寬螢幕點通知：面板裡的時間軸捲到那一則並閃一下', async () => {
    // Arrange
    const target = commented('面板裡的留言');
    serveProject();
    serveActivity('SMK-7', [created(), target]);

    // Act
    renderApp(`/projects/SMK?item=SMK-7${commentHash(target.id)}`);

    // Assert
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-7' });
    await waitFor(() => {
      expect(highlighted()).toBe(within(panel).getByText('面板裡的留言').closest('li'));
    });
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
  });

  it('同一頁兩個時間軸（項目頁加面板）：只有含目標的那一個動，另一個不往下載', async () => {
    // Arrange
    const target = commented('SMK-8 的留言');
    serveProject();
    const page = serveActivity('SMK-7', [created(), ...manyComments(60)]);
    serveActivity('SMK-8', [created(), target]);

    // Act：整頁是 SMK-7，旁邊的面板是 SMK-8，hash 指的是面板裡的留言
    renderApp(`/projects/SMK/items/SMK-7?item=SMK-8${commentHash(target.id)}`);

    // Assert
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-8' });
    await waitFor(() => {
      expect(highlighted()).toBe(within(panel).getByText('SMK-8 的留言').closest('li'));
    });
    await screen.findByText('留言 49');
    expect(page.requests).toHaveLength(1);
  });

  it('面板開著別的項目時，整頁的時間軸不認那個 hash', async () => {
    // Arrange
    const target = commented('整頁的留言');
    serveProject();
    serveActivity('SMK-7', [created(), target]);
    serveActivity('SMK-8', [created()]);

    // Act
    renderApp(`/projects/SMK/items/SMK-7?item=SMK-8${commentHash(target.id)}`);

    // Assert
    await screen.findByText('整頁的留言');
    await screen.findByRole('complementary', { name: '項目 SMK-8' });
    expect(scrollIntoView).not.toHaveBeenCalled();
    expect(highlighted()).toBeNull();
  });
});
