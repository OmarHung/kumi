import { screen, waitFor, within } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ItemDetail } from '../../api/types';
import { serveActivity } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { descriptionRevisionOf, detailOf, serveItem, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { clearEditor, findEditor, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';

const withoutDescription = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));
const withDescription = detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '舊的第一行\n舊的第二行');

async function openDescription(item: ItemDetail) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(item);
  serveActivity(item.key, []);
  const patches = servePatch(item);
  const view = renderApp(`/projects/SMK/items/${item.key}`);
  const section = await screen.findByRole('region', { name: '說明' });
  return { ...view, section, patches };
}

describe('說明', () => {
  it('沒有說明時提示並可以新增；儲存後保留換行顯示，焦點回到編輯按鈕', async () => {
    // Arrange
    const { user, section, patches } = await openDescription(withoutDescription);
    // 能編輯的人看到的虛線框就是「新增說明」（不再寫「還沒有說明」，見 DescriptionEntry.test.tsx）。
    expect(within(section).getByRole('button', { name: '新增說明' })).toHaveTextContent(/^新增說明/);

    // Act
    await user.click(within(section).getByRole('button', { name: '新增說明' }));
    const editor = await findEditor('說明', section);
    await typeInEditor(user, editor, '第一行{Enter}第二行');
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert：Enter 是新段落，存的是 Markdown
    expect(await within(section).findByText('第二行')).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ description: '第一行\n\n第二行', expectedDescriptionRevision: descriptionRevisionOf(null) }]);
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '編輯說明' })).toHaveFocus();
    });
    expect(within(section).queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('編輯時帶著目前的內容，Ctrl+Enter 儲存', async () => {
    // Arrange
    const { user, section, patches } = await openDescription(withDescription);

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    const editor = await findEditor('說明', section);
    expect(editor).toHaveTextContent('舊的第一行');
    expect(editor).toHaveTextContent('舊的第二行');
    await typeInEditor(user, editor, '{Enter}新的一行');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    expect(await within(section).findByText('新的一行')).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ description: '舊的第一行\n舊的第二行\n\n新的一行', expectedDescriptionRevision: descriptionRevisionOf('舊的第一行\n舊的第二行') }]);
  });

  it('⌘+Enter 也能儲存', async () => {
    // Arrange
    const { user, section, patches } = await openDescription(withDescription);

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    await typeInEditor(user, await findEditor('說明', section), '！');
    await user.keyboard('{Meta>}{Enter}{/Meta}');

    // Assert
    expect(await within(section).findByRole('button', { name: '編輯說明' })).toBeInTheDocument();
    expect(patches).toHaveLength(1);
  });

  it('清空後儲存就是清掉說明', async () => {
    // Arrange
    const { user, section, patches } = await openDescription(withDescription);

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    await clearEditor(user, await findEditor('說明', section));
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert：存成空白之後入口是「新增說明」那一塊，焦點跟過去
    expect(await within(section).findByRole('button', { name: '新增說明' })).toHaveFocus();
    expect(patches.map((request) => request.body)).toEqual([{ description: null, expectedDescriptionRevision: descriptionRevisionOf('舊的第一行\n舊的第二行') }]);
  });

  it('Esc 或「取消」放棄修改，不送出，顯示原本的內容', async () => {
    // Arrange
    const { user, section, patches } = await openDescription(withDescription);

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    await typeInEditor(user, await findEditor('說明', section), '改到一半');
    await user.keyboard('{Escape}');

    // Assert
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '編輯說明' })).toHaveFocus();
    });
    expect(within(section).queryByText(/改到一半/)).not.toBeInTheDocument();

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    await typeInEditor(user, await findEditor('說明', section), '又改到一半');
    await user.click(within(section).getByRole('button', { name: '取消' }));

    // Assert
    expect(within(section).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(section).queryByText(/改到一半/)).not.toBeInTheDocument();
    expect(patches).toHaveLength(0);
  });

  it('沒有改就儲存不送出', async () => {
    // Arrange
    const { user, section, patches } = await openDescription(withDescription);

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    expect(within(section).getByRole('button', { name: '編輯說明' })).toBeInTheDocument();
    expect(patches).toHaveLength(0);
  });

  it('失敗時留著輸入的內容，訊息和文字框連在一起，可以再送一次', async () => {
    // Arrange
    const { user, section } = await openDescription(withDescription);
    server.use(
      http.patch('/v1/items/SMK-7', () =>
        failure(422, 'item.description_too_long', '資料有問題。', [
          { code: 'item.description_too_long', path: 'description', message: '說明最多 50,000 個字。' },
        ]),
      ),
    );

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    const editor = await findEditor('說明', section);
    await typeInEditor(user, editor, '{Enter}很長的內容');
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(section).findByRole('alert')).toHaveTextContent('說明最多 50,000 個字。');
    expect(editor).toHaveTextContent('很長的內容');
    expect(editor).toHaveAttribute('aria-invalid', 'true');
    expect(editor).toHaveAccessibleDescription(/說明最多 50,000 個字。/);
    expect(within(section).getByRole('button', { name: '儲存' })).toBeEnabled();
  });

  it('按「儲存」時焦點先回到編輯器（儲存中按鈕停用，不然焦點掉到頁面本體）；失敗後再打字，錯誤訊息收掉', async () => {
    // Arrange
    const { user, section } = await openDescription(withDescription);
    server.use(
      http.patch('/v1/items/SMK-7', () =>
        failure(422, 'item.description_too_long', '資料有問題。', [
          { code: 'item.description_too_long', path: 'description', message: '說明最多 50,000 個字。' },
        ]),
      ),
    );
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    const editor = await findEditor('說明', section);
    await typeInEditor(user, editor, '{Enter}很長的內容');

    // Act
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(section).findByRole('alert')).toHaveTextContent('說明最多 50,000 個字。');
    expect(editor).toHaveFocus();

    // Act：再打一個字
    await user.keyboard('！');

    // Assert
    await waitFor(() => {
      expect(within(section).queryByRole('alert')).not.toBeInTheDocument();
    });
    expect(editor).not.toHaveAttribute('aria-invalid');
  });

  it('接近上限時顯示還可以輸入幾個字', async () => {
    // Arrange
    const { user, section } = await openDescription(withoutDescription);
    await user.click(within(section).getByRole('button', { name: '新增說明' }));
    const editor = await findEditor('說明', section);
    expect(within(section).queryByText(/還可以輸入/)).not.toBeInTheDocument();

    // Act
    await user.click(editor);
    await user.paste('字'.repeat(49_000));

    // Assert
    expect(await within(section).findByText('還可以輸入 1,000 個字')).toBeInTheDocument();
    expect(editor).toHaveAccessibleDescription(/還可以輸入 1,000 個字/);
  });

  it('打開編輯器不改就儲存：不送出請求，內容還是原本那一段', async () => {
    // Arrange：這段 Markdown 轉回去會被正規化（* 變 -），沒動過就要送原文，所以判斷成沒改
    const { user, section, patches } = await openDescription(detailOf(smkItem({ number: 7 }), '* 第一項\n* 第二項'));

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    await findEditor('說明', section);
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '編輯說明' })).toHaveFocus();
    });
    expect(patches).toHaveLength(0);
  });

  it('打字儲存送出的是 Markdown：工具列的粗體、輸入規則的清單', async () => {
    // Arrange
    const { user, section, patches } = await openDescription(withoutDescription);

    // Act
    await user.click(within(section).getByRole('button', { name: '新增說明' }));
    const editor = await findEditor('說明', section);
    await typeInEditor(user, editor, '- 甲{Enter}乙');
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    await waitFor(() => {
      expect(patches).toHaveLength(1);
    });
    expect(patches[0]?.body).toEqual({ description: '- 甲\n- 乙', expectedDescriptionRevision: descriptionRevisionOf(null) });
  });

  it('內容照 Markdown 顯示，原始的標籤照字面顯示', async () => {
    // Arrange & Act
    const { section } = await openDescription(detailOf(smkItem({ number: 7 }), '<b>粗體</b>'));

    // Assert
    expect(within(section).getByText('<b>粗體</b>')).toBeInTheDocument();
  });
});
