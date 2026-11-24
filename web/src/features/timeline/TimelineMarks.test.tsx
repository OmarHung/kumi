import { act, fireEvent, screen, within } from '@testing-library/react';
import { http } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ok, signedInAs } from '../../test/api';
import { exampleOwner, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { cycleOf, milestoneOf, serveCycles, serveMilestones } from '../../test/planning';
import { server } from '../../test/server';
import { renderTimeline } from '../../test/timeline';

// 今天 2026-11-18：預設畫面 11/09 ～ 12/20。
const BAR = smkItem({ number: 1, title: '首頁設計稿', state: 'doing', stateCategory: 'in_progress', startDate: '2026-11-18', dueDate: '2026-11-28' });
const POINT = smkItem({ number: 2, title: '內容審稿', dueDate: '2026-11-25' });

afterEach(() => {
  vi.unstubAllGlobals();
});

function serveUndated(items: readonly ReturnType<typeof smkItem>[]) {
  server.use(
    http.get('/v1/projects/SMK/items', ({ request }) => {
      if (new URL(request.url).searchParams.get('noDue') !== 'true') return undefined;
      return ok({ items, nextCursor: null });
    }),
  );
}

async function open(items = [BAR, POINT], path?: string) {
  signedInAs(exampleOwner);
  serveItems('SMK', items);
  serveUndated([]);
  const view = renderTimeline({ path });
  await screen.findAllByRole('link', { name: /^SMK-/ });
  return view;
}

function varOf(element: HTMLElement | null, name: string): string {
  return element?.style.getPropertyValue(name) ?? '';
}

describe('條與點的位置', () => {
  it('位置是天數：點在到期日那一天（一天寬），條從開始日畫到到期日，不依賴命中範圍的寬度', async () => {
    // Arrange & Act
    await open();

    // Assert
    const point = screen.getByTestId('mark-SMK-2').parentElement;
    expect(varOf(point, '--col')).toBe('16');
    expect(varOf(point, '--span')).toBe('1');
    const bar = screen.getByTestId('mark-SMK-1').parentElement;
    expect(varOf(bar, '--col')).toBe('9');
    expect(varOf(bar, '--span')).toBe('11');
  });

  it('月刻度的單日條就是一天寬（最小寬度只算在命中範圍，不撐開條）', async () => {
    // Arrange & Act
    await open([smkItem({ number: 3, title: '單日', startDate: '2026-11-20', dueDate: '2026-11-20' })], '/projects/SMK?view=timeline&scale=month');

    // Assert
    expect(varOf(screen.getByTestId('mark-SMK-3').parentElement, '--span')).toBe('1');
  });
});

describe('條的提示與互動', () => {
  it('條有 title（編號 標題，日期），aria-hidden 的元素滑鼠照樣看得到', async () => {
    // Arrange & Act
    await open();

    // Assert
    expect(screen.getByTestId('mark-SMK-1')).toHaveAttribute('title', 'SMK-1 首頁設計稿，11/18～11/28');
  });

  it('左欄的連結有 title，截斷的名稱看得到全文', async () => {
    // Arrange & Act
    await open();

    // Assert
    const label = screen.getByRole('link', { name: 'SMK-1 首頁設計稿，11/18～11/28' });
    expect(label).toHaveAttribute('title', 'SMK-1 首頁設計稿，11/18～11/28');
  });

  it('按下條不讓焦點落在 aria-hidden 的連結上', async () => {
    // Arrange
    await open();
    const mark = screen.getByTestId('mark-SMK-1');

    // Act
    const notPrevented = fireEvent.mouseDown(mark);

    // Assert
    expect(notPrevented).toBe(false);
  });

  it('左欄的連結取得焦點或被滑過時，同一列的條也標示出來；離開就取消', async () => {
    // Arrange
    const { user } = await open();
    const label = screen.getByRole('link', { name: /SMK-1 首頁設計稿/ });
    const mark = screen.getByTestId('mark-SMK-1');
    expect(mark).toHaveAttribute('data-active', 'false');

    // Act & Assert
    await user.hover(label);
    expect(mark).toHaveAttribute('data-active', 'true');
    await user.unhover(label);
    expect(mark).toHaveAttribute('data-active', 'false');
    act(() => {
      label.focus();
    });
    expect(mark).toHaveAttribute('data-active', 'true');
    expect(screen.getByTestId('mark-SMK-2')).toHaveAttribute('data-active', 'false');
    act(() => {
      label.blur();
    });
    expect(mark).toHaveAttribute('data-active', 'false');
  });

  it('滑過條時左欄那一列也標示出來', async () => {
    // Arrange
    const { user } = await open();

    // Act
    await user.hover(screen.getByTestId('mark-SMK-2'));

    // Assert
    expect(screen.getByRole('link', { name: /SMK-2/ })).toHaveAttribute('data-active', 'true');
    expect(screen.getByRole('link', { name: /SMK-1/ })).toHaveAttribute('data-active', 'false');
  });

  it('面板開著的那一筆（aria-current），右邊的條標成選取', async () => {
    // Arrange
    const { user } = await open();

    // Act
    await user.click(screen.getByRole('link', { name: /SMK-1 首頁設計稿/ }));

    // Assert
    expect(await screen.findByTestId('mark-SMK-1')).toHaveAttribute('data-selected', 'true');
    expect(screen.getByTestId('mark-SMK-2')).toHaveAttribute('data-selected', 'false');
  });
});

describe('週期與里程碑的表頭', () => {
  async function openWith(cycles: ReturnType<typeof cycleOf>[], milestones: ReturnType<typeof milestoneOf>[]) {
    signedInAs(exampleOwner);
    serveCycles('SMK', cycles);
    serveMilestones('SMK', milestones);
    serveItems('SMK', [BAR]);
    serveUndated([]);
    renderTimeline();
    await screen.findAllByRole('link', { name: /^SMK-/ });
    return screen.getByTestId('timeline-scroller');
  }

  it('這段時間沒有週期、里程碑時，表頭不畫那兩排', async () => {
    // Arrange & Act
    const scroller = await openWith([], []);

    // Assert
    expect(scroller).toHaveAttribute('data-has-cycles', 'false');
    expect(scroller).toHaveAttribute('data-has-milestones', 'false');
  });

  it('有的時候才畫，名稱被截斷時看得到全文（title）', async () => {
    // Arrange & Act
    const scroller = await openWith([cycleOf('衝刺 2', '2026-11-16', '2026-11-29')], [milestoneOf('年終檔期上線', '2026-12-01')]);

    // Assert
    await screen.findByTitle('衝刺 2 11/16～11/29');
    expect(scroller).toHaveAttribute('data-has-cycles', 'true');
    expect(scroller).toHaveAttribute('data-has-milestones', 'true');
    expect(screen.getByTitle('年終檔期上線，12/01')).toBeInTheDocument();
  });

  it('週期完全在這段時間以外時不算有週期', async () => {
    // Arrange & Act
    const scroller = await openWith([cycleOf('舊的', '2026-01-05', '2026-01-18')], []);

    // Assert
    expect(scroller).toHaveAttribute('data-has-cycles', 'false');
  });
});

describe('提示的位置', () => {
  it('沒有日期的提示放在時間軸下面，不佔表頭上方的高度', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveItems('SMK', [BAR]);
    serveUndated([smkItem({ number: 8 })]);
    renderTimeline();

    // Act
    const link = await screen.findByRole('link', { name: '1 個項目沒有開始日也沒有到期日' });

    // Assert
    const scroller = screen.getByTestId('timeline-scroller');
    expect(scroller.compareDocumentPosition(link) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

describe('「往後」的步進', () => {
  it('量得到畫面看得到幾天時，用可見的天數（不跳過沒看過的日子）；起點仍是週一', async () => {
    // Arrange：捲動區 1000 寬、左欄 288、一天 32 → 看得到 22 天，步進 14 天。
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (this: HTMLElement) {
      return this.dataset.testid === 'timeline-scroller' ? 1000 : 0;
    });
    vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockImplementation(function (this: HTMLElement) {
      if (this.dataset.testid === 'timeline-labels') return 288;
      return this.dataset.testid === 'timeline-track' ? 42 * 32 : 0;
    });
    await open();

    // Assert
    expect(await screen.findByRole('link', { name: /往後/ })).toHaveAttribute('href', expect.stringContaining('from=2026-11-23'));
    expect(screen.getByRole('link', { name: /往前/ })).toHaveAttribute('href', expect.stringContaining('from=2026-10-26'));
  });
});

describe('畫面跨年', () => {
  it('月刻度的半年跨年時，列名的日期帶年份', async () => {
    // Arrange & Act
    await open([smkItem({ number: 5, title: '跨年', startDate: '2026-12-28', dueDate: '2027-01-05' })], '/projects/SMK?view=timeline&scale=month');

    // Assert
    const list = screen.getByRole('list', { name: '項目' });
    expect(within(list).getByRole('link', { name: 'SMK-5 跨年，2026/12/28～2027/01/05' })).toBeInTheDocument();
  });
});

describe('單日條的命中範圍', () => {
  it('月刻度的單日條：連結本身至少 --target-min 寬，看得到的條畫在內層（寬度照天數）', async () => {
    // Arrange & Act
    await open([smkItem({ number: 3, title: '單日', startDate: '2026-11-20', dueDate: '2026-11-20' })], '/projects/SMK?view=timeline&scale=month');

    // Assert
    const link = screen.getByTestId('mark-SMK-3');
    expect(link).toHaveAttribute('data-kind', 'bar');
    const body = link.querySelector('[data-mark-body]');
    expect(body).not.toBeNull();
    expect(varOf(link.parentElement, '--span')).toBe('1');
  });
});
