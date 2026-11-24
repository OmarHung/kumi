import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TableColumns, TableScroll } from './Table';

/** 假的 ResizeObserver：記下回呼，測試用 resize() 模擬容器或表格變了大小。 */
function stubResizeObserver() {
  const callbacks: (() => void)[] = [];
  vi.stubGlobal(
    'ResizeObserver',
    class {
      constructor(callback: () => void) {
        callbacks.push(callback);
      }
      observe() {
        return undefined;
      }
      disconnect() {
        return undefined;
      }
    },
  );
  return {
    resize() {
      act(() => {
        callbacks.forEach((callback) => {
          callback();
        });
      });
    },
  };
}

/** 表格本身的寬度（getBoundingClientRect）與容器裡面的寬度（clientWidth）。 */
function stubWidths(tableWidth: number, clientWidth: number) {
  vi.spyOn(HTMLTableElement.prototype, 'getBoundingClientRect').mockReturnValue(DOMRect.fromRect({ x: 0, y: 0, width: tableWidth, height: 100 }));
  vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(clientWidth);
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('TableScroll', () => {
  it('是有名稱、用鍵盤停得到的區塊（表格比畫面寬時可以用方向鍵捲動）', () => {
    // Arrange / Act
    render(
      <TableScroll label="項目表格">
        <table aria-label="項目" />
      </TableScroll>,
    );

    // Assert
    const region = screen.getByRole('region', { name: '項目表格' });
    expect(region).toHaveAttribute('tabindex', '0');
    expect(region).toHaveAttribute('data-layout', 'inline');
    expect(screen.getByRole('table', { name: '項目' })).toBeInTheDocument();
  });

  it('清單頁（page）：表格放得下時不當捲動容器，表頭才黏得住頁列；放不下時才橫向捲動', () => {
    // Arrange
    const observer = stubResizeObserver();
    stubWidths(600, 800);
    render(
      <TableScroll label="我的工作" layout="page">
        <table aria-label="我的工作" />
      </TableScroll>,
    );
    const region = screen.getByRole('region', { name: '我的工作' });
    expect(region).toHaveAttribute('data-overflowing', 'false');

    // Act
    stubWidths(1200, 800);
    observer.resize();

    // Assert
    expect(region).toHaveAttribute('data-overflowing', 'true');
  });

  it('沒有 ResizeObserver 的環境當成放得下', () => {
    // Arrange / Act
    vi.stubGlobal('ResizeObserver', undefined);
    render(
      <TableScroll label="我的工作" layout="page">
        <table aria-label="我的工作" />
      </TableScroll>,
    );

    // Assert
    expect(screen.getByRole('region', { name: '我的工作' })).toHaveAttribute('data-overflowing', 'false');
  });

  it('工作區（fill）只標版面，不量寬度', () => {
    // Arrange / Act
    render(
      <TableScroll label="項目表格" layout="fill">
        <table aria-label="項目" />
      </TableScroll>,
    );

    // Assert
    const region = screen.getByRole('region', { name: '項目表格' });
    expect(region).toHaveAttribute('data-layout', 'fill');
    expect(region).not.toHaveAttribute('data-overflowing');
  });

  it('表格自己的格子都停得到（isFocusable=false）時，區塊不再多一個 Tab 停駐點，名稱照舊', () => {
    // Arrange / Act
    render(
      <TableScroll label="項目表格" layout="fill" isFocusable={false}>
        <table aria-label="項目" />
      </TableScroll>,
    );

    // Assert
    const region = screen.getByRole('region', { name: '項目表格' });
    expect(region).not.toHaveAttribute('tabindex');
  });

  it('frame={false}：不畫上下的外框（一個區塊裡放好幾張各自收邊的表格），版面照舊；預設畫外框', () => {
    // Arrange / Act
    render(
      <>
        <TableScroll label="我的工作" layout="page" frame={false}>
          <table aria-label="已過期" />
        </TableScroll>
        <TableScroll label="人員">
          <table aria-label="人員" />
        </TableScroll>
      </>,
    );

    // Assert
    const bare = screen.getByRole('region', { name: '我的工作' });
    expect(bare).toHaveAttribute('data-frame', 'false');
    expect(bare).toHaveAttribute('data-layout', 'page');
    expect(screen.getByRole('region', { name: '人員' })).not.toHaveAttribute('data-frame');
  });

  it('執行時才算得出來的欄寬變數（variables）設在區塊上，裡面的欄寬、固定欄、捲動讓出的寬度都看得到', () => {
    // Arrange / Act
    render(
      <TableScroll label="項目表格" layout="fill" variables={{ '--item-title-width': '320px' }}>
        <table aria-label="項目" />
      </TableScroll>,
    );

    // Assert
    expect(screen.getByRole('region', { name: '項目表格' }).style.getPropertyValue('--item-title-width')).toBe('320px');
  });
});

describe('TableColumns', () => {
  it('拉過的欄是那個寬度，其他的用預設寬度的 class；最後一欄後面一個空的欄吃掉剩下的寬度', () => {
    // Arrange / Act
    render(
      <table aria-label="範例">
        <TableColumns
          columns={[
            { key: 'key', className: 'colKey' },
            { key: 'due', className: 'colDue', width: 180 },
          ]}
          hasFiller
        />
        <tbody>
          <tr>
            <td>EX-1</td>
            <td>2026/12/15</td>
          </tr>
        </tbody>
      </table>,
    );

    // Assert：<col> 沒有角色，只能看 DOM（jsdom 沒有版面，實際的寬度在瀏覽器量）
    const columns = Array.from(screen.getByRole('table', { name: '範例' }).querySelectorAll('col'));
    expect(columns.map((column) => [column.className, column.style.width])).toEqual([
      ['colKey', ''],
      ['colDue', '180px'],
      ['', ''],
    ]);
  });
});
