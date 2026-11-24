import { createEvent, fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { http } from 'msw';
import { ok, signedInAs } from '../../test/api';
import { server } from '../../test/server';
import { agyDetail, exampleMember, exampleOwner } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { rowDetail, serveItemImport, utf16Bytes, type ItemImportOptions } from '../../test/itemImport';
import { chooseOption } from '../../test/select';
import { currentPath, renderApp } from '../../test/renderApp';
import { DRY_RUN_DEBOUNCE_MS } from './useImportCheck';

afterEach(() => {
  vi.unstubAllGlobals();
});

const HEADER = '標題,類型,負責人,到期日';

async function openImport(detail: ProjectDetail = agyDetail(), options: ItemImportOptions = {}) {
  signedInAs(exampleOwner, { details: [detail] });
  serveItems('AGY', []);
  const served = serveItemImport('AGY', options);
  const view = renderApp('/projects/AGY/import');
  await screen.findByRole('heading', { level: 1, name: '匯入項目' });
  return { ...view, served };
}

async function pickFile(user: UserEvent, content: string | Uint8Array<ArrayBuffer>, name = '匯入.csv', type = 'text/csv') {
  const input = await screen.findByLabelText('匯入的檔案');
  await user.upload(input, new File([content], name, { type }));
}

async function importButton(name: RegExp | string) {
  return screen.findByRole('button', { name });
}

describe('匯入項目頁：選檔案', () => {
  it('選了 CSV 之後顯示檔名、列數，並出現對應欄位的表', async () => {
    // Arrange
    const { user } = await openImport();

    // Act
    await pickFile(user, `${HEADER}\n登入頁,任務,${exampleOwner.displayName},2026/11/30\n`, '需求.csv');

    // Assert
    expect(await screen.findByText('需求.csv')).toBeInTheDocument();
    expect(screen.getByText(/1 列資料/)).toBeInTheDocument();
    expect(await screen.findByRole('table', { name: '欄位對應' })).toBeInTheDocument();
  });

  it('拖曳檔案進來和選檔案一樣', async () => {
    // Arrange
    await openImport();
    const file = new File([`${HEADER}\n甲,任務,,\n`], '拖.csv', { type: 'text/csv' });
    const zone = screen.getByText(/拖到這裡/).parentElement as HTMLElement;

    // Act
    fireEvent.drop(zone, { dataTransfer: { files: [file] } });

    // Assert
    expect(await screen.findByText('拖.csv')).toBeInTheDocument();
  });

  it('超過 5 MB 時說明，不出現對應表', async () => {
    // Arrange
    const { user } = await openImport();

    // Act
    await pickFile(user, new Uint8Array(5 * 1024 * 1024 + 1));

    // Assert
    expect(await screen.findByText(/超過上限 5 MB/)).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: '欄位對應' })).not.toBeInTheDocument();
  });

  it('超過 1,000 列時說明，不往下', async () => {
    // Arrange
    const { user } = await openImport();
    const rows = Array.from({ length: 1001 }, (_, index) => `項目 ${String(index)}`).join('\n');

    // Act
    await pickFile(user, `標題\n${rows}`);

    // Assert
    expect(await screen.findByText(/一次最多匯入 1,000 列/)).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: '欄位對應' })).not.toBeInTheDocument();
  });

  it('Big5 的檔案寫「以 Big5 讀取」', async () => {
    // Arrange：「中文」的 Big5
    const { user } = await openImport();

    // Act
    await pickFile(user, new Uint8Array([0xa4, 0xa4, 0xa4, 0xe5, 0x0a, 0xa4, 0xa4]));

    // Assert
    expect(await screen.findByText(/以 Big5 讀取/)).toBeInTheDocument();
  });

  it('下載範本：UTF-8 加 BOM，一列標題', async () => {
    // Arrange
    const { user } = await openImport();
    const blobs: Blob[] = [];
    vi.stubGlobal('URL', { ...URL, createObjectURL: (blob: Blob) => (blobs.push(blob), 'blob:x'), revokeObjectURL: () => undefined });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    // Act
    await user.click(screen.getByRole('button', { name: '下載範本' }));

    // Assert
    const text = await new Promise<Uint8Array>((resolve) => {
      const reader = new FileReader();
      reader.onload = () => {
        resolve(new Uint8Array(reader.result as ArrayBuffer));
      };
      reader.readAsArrayBuffer(blobs[0] as Blob);
    });
    expect([...text.slice(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
    const decoded = new TextDecoder('utf-8', { ignoreBOM: true }).decode(text);
    expect(decoded.startsWith('\uFEFF標題,類型,狀態,負責人')).toBe(true);
    expect(decoded).toContain('報價金額');
  });
});

describe('匯入項目頁：對應欄位', () => {
  it('標題列自動對應，可以改；另一欄選了同一個目標時原本那一欄換成不匯入', async () => {
    // Arrange
    const { user } = await openImport();
    await pickFile(user, '標題,說明\n甲,乙\n');
    const titleSelect = await screen.findByRole('combobox', { name: '「標題」要匯入成' });
    const descSelect = screen.getByRole('combobox', { name: '「說明」要匯入成' });
    expect(titleSelect).toHaveTextContent('標題');
    expect(descSelect).toHaveTextContent('說明');

    // Act
    await chooseOption(user, descSelect, '標題');

    // Assert
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: '「標題」要匯入成' })).toHaveTextContent('不匯入');
    });
  });

  it('沒有對應到標題時說明，不往下', async () => {
    // Arrange
    const { user } = await openImport();

    // Act
    await pickFile(user, '備註\n甲\n');

    // Assert
    expect(await screen.findByText(/要有一欄匯入成「標題」/)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: '檢查結果' })).not.toBeInTheDocument();
  });

  it('沒有對應到類型時有「全部匯入成」，預設是第一個類型', async () => {
    // Arrange
    const { user } = await openImport();

    // Act
    await pickFile(user, '標題\n甲\n');

    // Assert
    expect(await screen.findByRole('combobox', { name: '全部匯入成' })).toHaveTextContent('需求');
  });

  it('有類型欄位時不再出現「全部匯入成」', async () => {
    // Arrange
    const { user } = await openImport();

    // Act
    await pickFile(user, '標題,類型\n甲,任務\n');

    // Assert
    await screen.findByRole('table', { name: '欄位對應' });
    expect(screen.queryByRole('combobox', { name: '全部匯入成' })).not.toBeInTheDocument();
  });

  it('沒有 project.manage 時「狀態」停用並說明，也不自動對應', async () => {
    // Arrange
    const detail = agyDetail({ myPermissions: ['item.read', 'item.create'] });
    const { user } = await openImport(detail);

    // Act
    await pickFile(user, '標題,狀態\n甲,開發\n');

    // Assert
    const select = await screen.findByRole('combobox', { name: '「狀態」要匯入成' });
    expect(select).toHaveTextContent('不匯入');
    expect(screen.getByText('只有專案管理員能匯入狀態。')).toBeInTheDocument();
    await user.click(select);
    const option = await screen.findByRole('option', { name: /狀態/ });
    expect(option).toHaveAttribute('aria-disabled', 'true');
  });
});

describe('匯入項目頁：檢查與匯入', () => {
  it('全部可以時顯示摘要、預覽與「匯入 N 個項目」，並送一次乾跑', async () => {
    // Arrange
    const { user, served } = await openImport();

    // Act
    await pickFile(user, `${HEADER}\n甲,任務,${exampleOwner.displayName},2026/11/30\n乙,需求,,\n`);

    // Assert
    expect(await screen.findByText('共 2 列；全部可以匯入')).toBeInTheDocument();
    expect(await importButton('匯入 2 個項目')).toBeEnabled();
    const preview = screen.getByRole('table', { name: '匯入內容的預覽' });
    expect(within(preview).getByText('2026-11-30')).toBeInTheDocument();
    await waitFor(() => {
      expect(served.requests[0]?.dryRun).toBe(true);
    });
    expect(served.requests[0]?.rows[0]).toMatchObject({ type: 'task', title: '甲', assigneeId: exampleOwner.personId, dueDate: '2026-11-30' });
  });

  it('網頁的問題與伺服器乾跑的問題合併顯示，列號是檔案的列號，按鈕略過那幾列', async () => {
    // Arrange：第 3 列的負責人找不到（網頁），第 4 列伺服器擋下（送出的第 1 筆）
    const { user } = await openImport(agyDetail(), {
      validate: (rows) => (rows.length > 0 ? [rowDetail(1, 'due_date', '到期日不能早於開始日。')] : null),
    });

    // Act
    await pickFile(user, `${HEADER}\n甲,任務,,\n乙,任務,王小明,\n丙,任務,,\n丁,任務,,\n`);

    // Assert
    expect(await screen.findByText('第 3 列「負責人」：找不到「王小明」這個成員')).toBeInTheDocument();
    expect(await screen.findByText('第 4 列「到期日」：到期日不能早於開始日。')).toBeInTheDocument();
    expect(screen.getByText('共 4 列；2 列可以匯入，2 列有問題')).toBeInTheDocument();
    expect(await importButton('匯入沒問題的 2 個項目（略過 2 列）')).toBeEnabled();
  });

  it('每一列都有問題時按鈕停用並說明原因', async () => {
    // Arrange
    const { user } = await openImport();

    // Act
    await pickFile(user, `${HEADER}\n甲,史詩,,\n`);

    // Assert
    expect(await screen.findByText('第 2 列「類型」：找不到「史詩」這個類型')).toBeInTheDocument();
    const button = screen.getByRole('button', { name: '匯入' });
    expect(button).toHaveAttribute('aria-disabled', 'true');
    const reason = screen.getByText(/沒有可以匯入的列/);
    expect(button).toHaveAttribute('aria-describedby', reason.id);
  });

  it('改對應之後重新檢查', async () => {
    // Arrange
    const { user, served } = await openImport();
    await pickFile(user, '標題,備註\n甲,高\n');
    await importButton('匯入 1 個項目');
    expect(served.requests).toHaveLength(1);

    // Act
    await chooseOption(user, screen.getByRole('combobox', { name: '「備註」要匯入成' }), '優先度');

    // Assert
    await waitFor(
      () => {
        expect(served.requests).toHaveLength(2);
      },
      { timeout: DRY_RUN_DEBOUNCE_MS + 2500 },
    );
    expect(served.requests[1]?.rows[0]).toMatchObject({ priority: 'high' });
  });

  it('送出中停用並寫「匯入中…」，成功後回到專案頁帶提示', async () => {
    // Arrange
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const { user, router } = await openImport(agyDetail(), { gate, firstNumber: 12 });
    await pickFile(user, '標題\n甲\n乙\n');

    // Act
    await user.click(await importButton('匯入 2 個項目'));

    // Assert
    const pending = await screen.findByRole('button', { name: '匯入中…' });
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).toHaveFocus();
    release();
    expect(await screen.findByText('已匯入 2 個項目（AGY-12～AGY-13）。')).toBeInTheDocument();
    expect(currentPath(router)).toMatch(/^\/projects\/AGY/);
    expect(currentPath(router)).not.toContain('import');
  });

  it('送出時伺服器回 422：留在這一頁，把問題併進結果', async () => {
    // Arrange：乾跑時過，正式送出時變成有問題
    let isDryRun = true;
    const { user, router } = await openImport(agyDetail(), {
      validate: (_rows, request) => {
        if (request.dryRun) return null;
        isDryRun = false;
        return [rowDetail(1, 'parent_key', '上層項目不存在。')];
      },
    });
    await pickFile(user, '標題\n甲\n乙\n');

    // Act
    await user.click(await importButton('匯入 2 個項目'));

    // Assert
    expect(await screen.findByText('第 3 列「上層項目」：上層項目不存在。')).toBeInTheDocument();
    expect(isDryRun).toBe(false);
    expect(currentPath(router)).toBe('/projects/AGY/import');
    expect(await importButton('匯入沒問題的 1 個項目（略過 1 列）')).toBeEnabled();
  });

  it('乾跑失敗（非 422）時說明並可以重新檢查', async () => {
    // Arrange
    const { user } = await openImport(agyDetail(), { reject: { status: 500, code: 'server.error', message: '伺服器暫時無法處理。' } });

    // Act
    await pickFile(user, '標題\n甲\n');

    // Assert
    expect(await screen.findByText('伺服器暫時無法處理，請稍後再試。')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '重新檢查' })).toBeInTheDocument();
  });
});

describe('匯入項目頁：失敗的情況', () => {
  it('乾跑遇到 project.archived 時讓專案失效，頁面換成封存的提示', async () => {
    // Arrange
    const archivedBase = agyDetail();
    const archived = agyDetail({ project: { ...archivedBase.project, archivedAt: '2026-11-18T00:00:00Z' } });
    const { user } = await openImport(agyDetail(), { reject: { status: 409, code: 'project.archived', message: '專案「接案測試」已封存，只能看不能改。' } });
    let isArchived = false;
    server.use(http.get('/v1/projects/AGY', () => ok(isArchived ? archived : agyDetail())));
    isArchived = true;

    // Act
    await pickFile(user, '標題\n甲\n');

    // Assert
    expect(await screen.findByText('這個專案已封存：只能看，不能新增或修改。')).toBeInTheDocument();
  });

  it.each([
    ['乾跑', false],
    ['送出', true],
  ])('%s遇到 413 時顯示伺服器的訊息，並說明分成幾次匯入', async (_name, onlySubmit) => {
    // Arrange
    const { user } = await openImport(agyDetail(), {
      reject: { onlySubmit, status: 413, code: 'import.too_large', message: '匯入的資料超過 16 MiB。' },
    });

    // Act
    await pickFile(user, '標題\n甲\n');
    if (onlySubmit) await user.click(await importButton('匯入 1 個項目'));

    // Assert
    expect(await screen.findByText('匯入的資料超過 16 MiB。請把檔案分成幾次匯入。')).toBeInTheDocument();
  });

  it('Excel 的 Unicode 文字（UTF-16）依 BOM 讀取並說明', async () => {
    // Arrange
    const { user } = await openImport();
    const text = '標題\t類型\n甲\t任務\n';
    const bytes = utf16Bytes(text);

    // Act
    await pickFile(user, bytes, 'unicode.txt', 'text/plain');

    // Assert
    expect(await screen.findByText(/以 UTF-16 讀取/)).toBeInTheDocument();
    expect(await screen.findByRole('combobox', { name: '「標題」要匯入成' })).toHaveTextContent('標題');
  });

  it('讀取中再拖一個檔案進來不收，結果不會被覆蓋', async () => {
    // Arrange
    await openImport();
    const zone = screen.getByText(/拖到這裡/).parentElement as HTMLElement;
    const first = new File(['標題\n甲\n'], 'a.csv', { type: 'text/csv' });
    const second = new File(['標題\n乙\n'], 'b.csv', { type: 'text/csv' });

    // Act
    fireEvent.drop(zone, { dataTransfer: { files: [first] } });
    fireEvent.drop(zone, { dataTransfer: { files: [second] } });

    // Assert
    expect(await screen.findByText('a.csv')).toBeInTheDocument();
    expect(screen.queryByText('b.csv')).not.toBeInTheDocument();
  });

  it('游標移進拖曳區裡面的元素時，高亮不會取消', async () => {
    // Arrange
    await openImport();
    const zone = screen.getByText(/拖到這裡/).parentElement as HTMLElement;
    fireEvent.dragOver(zone);
    expect(zone).toHaveAttribute('data-dragging');

    // Act
    // jsdom 沒有 DragEvent，relatedTarget 要自己補在事件上。
    const leave = createEvent.dragLeave(zone);
    Object.defineProperty(leave, 'relatedTarget', { value: screen.getByRole('button', { name: '選擇檔案' }) });
    fireEvent(zone, leave);

    // Assert
    expect(zone).toHaveAttribute('data-dragging');
    fireEvent.dragLeave(zone);
    expect(zone).not.toHaveAttribute('data-dragging');
  });
});

describe('匯入項目頁：權限與專案狀態', () => {
  it('沒有 item.create 時顯示 403 的頁面', async () => {
    // Arrange
    signedInAs(exampleMember, { details: [agyDetail({ myPermissions: ['item.read'] })] });
    renderApp('/projects/AGY/import');

    // Act & Assert
    expect(await screen.findByText('沒有權限匯入項目')).toBeInTheDocument();
    expect(screen.queryByLabelText('匯入的檔案')).not.toBeInTheDocument();
  });

  it('封存的專案顯示共用的封存提示（有 project.manage 時旁邊有「取消封存」），不能匯入', async () => {
    // Arrange
    const base = agyDetail();
    const archived = agyDetail({ project: { ...base.project, archivedAt: '2026-11-18T00:00:00Z' } });
    signedInAs(exampleOwner, { details: [archived] });

    // Act
    renderApp('/projects/AGY/import');

    // Assert
    expect(await screen.findByText('這個專案已封存：只能看，不能新增或修改。')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '取消封存' })).toBeInTheDocument();
    expect(screen.queryByLabelText('匯入的檔案')).not.toBeInTheDocument();
  });
});
