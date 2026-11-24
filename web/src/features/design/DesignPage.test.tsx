import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { renderApp } from '../../test/renderApp';
import { openOptions } from '../../test/select';

afterEach(() => {
  document.documentElement.removeAttribute('data-theme');
});

describe('/design 展示頁（只在開發模式）', () => {
  it('不必登入就看得到每一類共用元件', async () => {
    // Arrange
    renderApp('/design');

    // Act
    const heading = await screen.findByRole('heading', { level: 1, name: '設計元件' });

    // Assert
    expect(heading).toBeInTheDocument();
    const sections = screen.getAllByRole('heading', { level: 2 }).map((section) => section.textContent);
    expect(sections).toEqual(['按鈕與欄位', '提示與狀態', '標籤與頭像', '選單、浮層與對話框', '編輯器', '導覽與版面', '設定頁的區塊', '寄件人', '登出其他裝置', '圖示']);
  });

  it('設定頁的區塊：兩塊，一塊有表單、一塊有按鈕，各是有名稱的 region', async () => {
    // Arrange
    renderApp('/design');

    // Act
    const demo = await screen.findByRole('region', { name: '設定頁的區塊' });

    // Assert
    const sender = within(demo).getByRole('region', { name: '寄件人' });
    expect(within(sender).getByRole('textbox', { name: '寄件人名稱' })).toHaveValue('範例組織');
    expect(within(sender).getByRole('button', { name: '儲存' })).toBeInTheDocument();
    const logout = within(demo).getByRole('region', { name: '登出其他裝置' });
    expect(within(logout).getByRole('button', { name: '登出其他裝置' })).toBeInTheDocument();
  });

  it('提示與狀態：新通知的提示按了才跳，一次五則時併成「還有 3 則新通知」', async () => {
    // Arrange
    const { user } = renderApp('/design');
    const section = await screen.findByRole('region', { name: '提示與狀態' });

    // Act
    await user.click(within(section).getByRole('button', { name: '一次跳五則' }));

    // Assert
    expect(await screen.findByRole('region', { name: '新通知' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '還有 3 則新通知' })).toBeInTheDocument();
  });

  it('編輯器：一般、可以插入檔案、提及、送出中、錯誤、收起來、填滿畫面高度各一個，延後載入完才出現可以編輯的元素', async () => {
    // Arrange
    renderApp('/design');

    // Act
    const section = await screen.findByRole('region', { name: '編輯器' });

    // Assert
    const names = within(section).getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent);
    expect(names).toEqual(['RichTextEditor', 'uploads', 'mentionMembers', 'isBusy', 'error', 'isCollapsed', 'form', 'fillsViewport']);
    expect(await within(section).findByRole('button', { name: '插入圖片或檔案' })).toBeInTheDocument();
    expect(await within(section).findByRole('textbox', { name: '說明' })).toHaveAttribute('contenteditable', 'true');
    expect(await within(section).findByRole('textbox', { name: '送出中' })).toHaveAttribute('aria-readonly', 'true');
    expect(await within(section).findByRole('textbox', { name: '有錯誤' })).toHaveAttribute('aria-invalid', 'true');
    expect(within(section).getByRole('button', { name: '展開' })).toBeInTheDocument();
  });

  it('標籤與頭像裡有 CountBadge 的兩種（中性、危險）', async () => {
    // Arrange
    renderApp('/design');

    // Act
    const labels = await screen.findByRole('region', { name: '標籤與頭像' });

    // Assert
    expect(within(labels).getByRole('heading', { level: 3, name: 'CountBadge' })).toBeInTheDocument();
    expect(within(labels).getByText('8')).toHaveAttribute('data-tone', 'neutral');
    expect(within(labels).getByText('2')).toHaveAttribute('data-tone', 'danger');
  });

  it('標籤與頭像裡有頭像的六種色調，每一個名稱一種', async () => {
    // Arrange
    renderApp('/design');

    // Act
    const labels = await screen.findByRole('region', { name: '標籤與頭像' });
    const toned = within(labels)
      .getAllByRole('img')
      .map((avatar) => avatar.getAttribute('data-tone'))
      .filter((tone) => tone !== null);

    // Assert
    expect(new Set(toned)).toEqual(new Set(['1', '2', '3', '4', '5', '6']));
  });

  it('按鈕與欄位裡有格子裡的下拉（bare）與自己畫的日期欄位（有值、格子裡的 bare），點了打開「選擇日期」', async () => {
    // Arrange
    const { user } = renderApp('/design');
    const controls = await screen.findByRole('region', { name: '按鈕與欄位' });
    const dueDate = within(controls).getByRole('combobox', { name: '到期日' });

    // Act
    await user.click(dueDate);
    const calendar = await screen.findByRole('dialog', { name: '選擇日期' });

    // Assert
    expect(within(controls).getByRole('combobox', { name: '格子裡的狀態' })).toHaveAttribute('data-variant', 'bare');
    expect(within(controls).getByRole('combobox', { name: '格子裡的到期日' })).toHaveAttribute('data-variant', 'bare');
    expect(dueDate).toHaveValue('2026/12/15');
    expect(dueDate).toHaveAttribute('aria-haspopup', 'dialog');
    expect(within(calendar).getByRole('grid', { name: '2026 年 12 月' })).toBeInTheDocument();
  });

  it('按鈕與欄位裡有密碼欄位：一般、有錯、停用，按「顯示新密碼」看得到內容', async () => {
    // Arrange
    const { user } = renderApp('/design');
    const controls = await screen.findByRole('region', { name: '按鈕與欄位' });
    const password = within(controls).getByLabelText('新密碼');

    // Act
    await user.click(within(controls).getByRole('button', { name: '顯示新密碼' }));

    // Assert
    expect(within(controls).getByRole('heading', { level: 3, name: 'PasswordField' })).toBeInTheDocument();
    expect(password).toHaveAttribute('type', 'text');
    expect(within(controls).getByLabelText('目前的密碼')).toHaveAttribute('aria-invalid', 'true');
    expect(within(controls).getByRole('button', { name: '顯示初始密碼' })).toBeDisabled();
  });

  it('可以切換主題，套到 <html data-theme>', async () => {
    // Arrange
    const { user } = renderApp('/design');
    const themes = await screen.findByRole('group', { name: '主題' });

    // Act
    await user.click(within(themes).getByRole('button', { name: '深色' }));

    // Assert
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark');
    expect(within(themes).getByRole('button', { name: '深色' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(themes).getByRole('button', { name: '跟著系統' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('選單、浮層與對話框都打得開', async () => {
    // Arrange
    const { user } = renderApp('/design');
    await screen.findByRole('heading', { level: 1, name: '設計元件' });

    // Act
    await user.click(screen.getByRole('button', { name: 'AGY-3 的更多動作' }));
    const menuItemCount = within(screen.getByRole('menu', { name: 'AGY-3 的更多動作' })).getAllByRole('menuitem').length;
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: '顯示的欄位' }));
    const isPopoverOpen = screen.queryByRole('dialog', { name: '顯示的欄位' }) !== null;
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: '打開有表格的對話框' }));

    // Assert
    expect(menuItemCount).toBeGreaterThan(2);
    expect(isPopoverOpen).toBe(true);
    expect(screen.getByRole('dialog', { name: '成員' })).toHaveAttribute('data-size', 'lg');
  });

  it('下拉的展示和實作的決定一致：負責人每個選項都有小圖（所有人、我、沒有負責人是佔位）、類別的選項有形狀、選項多的有篩選框', async () => {
    // Arrange
    const { user } = renderApp('/design');
    const controls = await screen.findByRole('region', { name: '按鈕與欄位' });
    const hasLeading = (option: HTMLElement) => option.firstElementChild?.getAttribute('aria-hidden') === 'true';

    // Act
    const assignees = await openOptions(user, within(controls).getByRole('combobox', { name: '負責人' }));
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: '打開一般的對話框' }));
    const categories = await openOptions(user, within(screen.getByRole('dialog', { name: '新增狀態' })).getByRole('combobox', { name: '類別' }));
    await user.keyboard('{Escape}');
    await user.click(within(screen.getByRole('dialog', { name: '新增狀態' })).getByRole('button', { name: '取消' }));
    await openOptions(user, within(controls).getByRole('combobox', { name: '上層項目' }));

    // Assert
    expect(assignees.every(hasLeading)).toBe(true);
    expect(categories.every((option) => option.querySelector('[aria-hidden="true"] [data-icon]') !== null)).toBe(true);
    expect(await screen.findByRole('combobox', { name: '篩選 上層項目' })).toHaveFocus();
  });

  it('對話框的展示：一般大小的表單（送出鈕停用但停得到）與還不能確認的確認', async () => {
    // Arrange
    const { user } = renderApp('/design');
    await screen.findByRole('heading', { level: 1, name: '設計元件' });

    // Act
    await user.click(screen.getByRole('button', { name: '打開一般的對話框' }));
    const form = screen.getByRole('dialog', { name: '新增狀態' });
    const submit = within(form).getByRole('button', { name: '新增狀態' });
    await user.click(within(form).getByRole('button', { name: '取消' }));
    await user.click(screen.getByRole('button', { name: '套用變更集…' }));

    // Assert
    expect(form).toHaveAttribute('data-size', 'md');
    expect(submit).toHaveAttribute('aria-disabled', 'true');
    expect(within(screen.getByRole('dialog', { name: '套用變更集 #3？' })).getByRole('button', { name: '套用' })).toHaveAccessibleDescription(
      '正在確認會動到哪些項目…',
    );
  });
});
