import { describe, expect, it } from 'vitest';
import { detectDelimiter, parseCsvTable } from './csv';

function cellsOf(source: string) {
  const table = parseCsvTable(source);
  return [table.header, ...table.rows].map((record) => record?.cells);
}

describe('parseCsvTable', () => {
  it('第一列是標題，其餘是資料列，列號是檔案的列號', () => {
    // Arrange & Act
    const table = parseCsvTable('標題,類型\n登入頁,任務\n報表,需求\n');

    // Assert
    expect(table.header).toEqual({ line: 1, cells: ['標題', '類型'] });
    expect(table.rows.map((row) => row.line)).toEqual([2, 3]);
    expect(table.rows[1]?.cells).toEqual(['報表', '需求']);
  });

  it('引號裡的逗號與換行留在同一格', () => {
    // Arrange & Act
    const rows = cellsOf('a,b\n"一,二","第一行\n第二行"\n');

    // Assert
    expect(rows[1]).toEqual(['一,二', '第一行\n第二行']);
  });

  it('兩個連續的引號是一個引號字元', () => {
    // Arrange & Act
    const rows = cellsOf('a\n"他說""好"""\n');

    // Assert
    expect(rows[1]).toEqual(['他說"好"']);
  });

  it('欄位中間的引號是一般字元', () => {
    // Arrange & Act
    const rows = cellsOf('a,b\n5" 螢幕,x\n');

    // Assert
    expect(rows[1]).toEqual(['5" 螢幕', 'x']);
  });

  it('CRLF 與單獨的 CR 都是換行，最後沒有換行也收進最後一列', () => {
    // Arrange & Act
    const rows = cellsOf('a,b\r\n1,2\r3,4');

    // Assert
    expect(rows).toEqual([['a', 'b'], ['1', '2'], ['3', '4']]);
  });

  it('開頭的 BOM 不算內容', () => {
    // Arrange & Act
    const table = parseCsvTable('\uFEFF標題,類型\n甲,乙');

    // Assert
    expect(table.header?.cells).toEqual(['標題', '類型']);
  });

  it('完全空白的列略過，但後面的列號照檔案算', () => {
    // Arrange & Act
    const table = parseCsvTable('標題\n甲\n\n , \n乙\n');

    // Assert
    expect(table.rows.map((row) => [row.line, row.cells[0]])).toEqual([[2, '甲'], [5, '乙']]);
  });

  it('引號裡有空白行時不會被當成空白列切開', () => {
    // Arrange & Act
    const table = parseCsvTable('a,b\n"x\n\ny",z\n');

    // Assert
    expect(table.rows).toHaveLength(1);
    expect(table.rows[0]?.cells).toEqual(['x\n\ny', 'z']);
  });

  it('第一行有 tab 而且沒有逗號時用 tab 分隔', () => {
    // Arrange & Act
    const table = parseCsvTable('標題\t說明\n甲\t乙,丙');

    // Assert
    expect(table.delimiter).toBe('\t');
    expect(table.rows[0]?.cells).toEqual(['甲', '乙,丙']);
  });

  it('引號沒有結束時標記起來', () => {
    // Arrange & Act
    const table = parseCsvTable('a,b\n"沒結束,x\ny');

    // Assert
    expect(table.hasUnclosedQuote).toBe(true);
  });

  it('空檔案沒有標題列', () => {
    // Arrange & Act
    const table = parseCsvTable('\n \n');

    // Assert
    expect(table.header).toBeNull();
    expect(table.rows).toEqual([]);
  });

  it('尾端的空格子保留（列的格數不變）', () => {
    // Arrange & Act
    const rows = cellsOf('a,b,c\n1,,\n');

    // Assert
    expect(rows[1]).toEqual(['1', '', '']);
  });
});

describe('detectDelimiter', () => {
  it('有逗號的第一行是 CSV，即使裡面也有 tab', () => {
    expect(detectDelimiter('a\tb,c\n1')).toBe(',');
  });
});
