import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ItemDetail } from '../../api/types';
import { serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { descriptionRevisionOf, detailOf, serveItem, servePatch } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { editorText, findEditor, typeInEditor } from '../../test/richText';

const withDescription = detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '原本的說明');

async function openDescription(item: ItemDetail = withDescription) {
  signedInAs(exampleOwner, { details: [smkDetail()] });
  serveItem(item);
  serveActivity(item.key, []);
  const patches = servePatch(item);
  const view = renderApp(`/projects/SMK/items/${item.key}`);
  const section = await screen.findByRole('region', { name: '說明' });
  return { ...view, section, patches };
}

async function leaveAndComeBack(view: Awaited<ReturnType<typeof openDescription>>): Promise<HTMLElement> {
  await view.user.click(within(screen.getByRole('navigation', { name: '主要導覽' })).getByRole('link', { name: '我的工作' }));
  await screen.findByRole('heading', { level: 1, name: '我的工作' });
  await view.router.navigate(-1);
  return screen.findByRole('region', { name: '說明' });
}

async function startEditing(view: Awaited<ReturnType<typeof openDescription>>, text: string) {
  await view.user.click(within(view.section).getByRole('button', { name: '編輯說明' }));
  await typeInEditor(view.user, await findEditor('說明', view.section), text);
}

/** 分頁裡存的草稿（Markdown）：不看編輯器的內容，看真正會被還原的東西。 */
function storedDraft(): string | null {
  const raw = sessionStorage.getItem('draft.description.SMK-7');
  if (raw === null) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed === 'object' && parsed !== null && 'text' in parsed && typeof parsed.text === 'string') return parsed.text;
  } catch {
    // 舊的純文字草稿不是 JSON。
  }
  return raw;
}

describe('說明的草稿', () => {
  it('編輯到一半換頁再回來：回到編輯中、內容還在，說明已還原；焦點不會被拉進文字框', async () => {
    // Arrange
    const view = await openDescription();
    await startEditing(view, '{Enter}補上的一行');

    // Act
    expect(storedDraft()).toBe('原本的說明\n\n補上的一行');
    const section = await leaveAndComeBack(view);

    // Assert
    // 還原編輯模式是頁面載入後才發生的，要等（負載高的 CI 上同步找會輸給載入）。
    const editor = await findEditor('說明', section);
    expect(editorText(editor)).toBe('原本的說明\n補上的一行');
    expect(editor).not.toHaveFocus();
    expect(await within(section).findByRole('status')).toHaveTextContent('已還原上次沒送出的內容。');
    expect(within(section).getByRole('button', { name: '捨棄還沒儲存的說明' })).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: '編輯說明' })).not.toBeInTheDocument();
  });

  it('「捨棄」回到伺服器上的內容、留在編輯中、焦點到文字框；再回來不會還原', async () => {
    // Arrange
    const view = await openDescription();
    await startEditing(view, '，不要了');
    const section = await leaveAndComeBack(view);

    // Act
    await view.user.click(await within(section).findByRole('button', { name: '捨棄還沒儲存的說明' }));

    // Assert
    const editor = await findEditor('說明', section);
    expect(editorText(editor)).toBe('原本的說明');
    await waitFor(() => {
      expect(editor).toHaveFocus();
    });
    expect(within(section).queryByRole('status')).not.toBeInTheDocument();

    // Act
    const again = await leaveAndComeBack(view);

    // Assert
    expect(within(again).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
    expect(within(again).getByRole('button', { name: '編輯說明' })).toBeInTheDocument();
  });

  it('和伺服器上的內容一樣就不算草稿（改了又改回去）', async () => {
    // Arrange
    const view = await openDescription();
    await startEditing(view, '！');
    await view.user.keyboard('{Backspace}');

    // Act
    const section = await leaveAndComeBack(view);

    // Assert
    expect(within(section).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
    expect(within(section).queryByText('已還原上次沒送出的內容。')).not.toBeInTheDocument();
  });

  it('Esc 取消就清掉草稿', async () => {
    // Arrange
    const view = await openDescription();
    await startEditing(view, '，打一半');

    // Act
    await view.user.keyboard('{Escape}');
    const section = await leaveAndComeBack(view);

    // Assert
    expect(within(section).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
    expect(within(section).getByText('原本的說明')).toBeInTheDocument();
  });

  it('儲存成功就清掉草稿；還原之後照樣可以 Ctrl+Enter 儲存', async () => {
    // Arrange
    const view = await openDescription();
    await startEditing(view, '，補充');
    const section = await leaveAndComeBack(view);
    await within(section).findByRole('status');

    // Act
    await view.user.click(await findEditor('說明', section));
    await view.user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    await waitFor(() => {
      expect(within(section).getByRole('button', { name: '編輯說明' })).toHaveFocus();
    });
    expect(view.patches.map((request) => request.body)).toEqual([{ description: '原本的說明，補充', expectedDescriptionRevision: descriptionRevisionOf('原本的說明') }]);

    // Act
    const again = await leaveAndComeBack(view);

    // Assert
    expect(within(again).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
  });

  it('存著的草稿和伺服器現在的內容一樣（例如別人剛好存了同樣的）就不還原', async () => {
    // Arrange
    sessionStorage.setItem('draft.description.SMK-7', '原本的說明');

    // Act
    const { section } = await openDescription();

    // Assert
    expect(within(section).queryByRole('textbox', { name: '說明' })).not.toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '編輯說明' })).toBeInTheDocument();
  });

  it('沒有說明時新增到一半，回來照樣還原', async () => {
    // Arrange
    const view = await openDescription(detailOf(smkItem({ number: 7, title: '首頁設計稿' })));
    await view.user.click(within(view.section).getByRole('button', { name: '新增說明' }));
    await typeInEditor(view.user, await findEditor('說明', view.section), '第一版說明');

    // Act
    const section = await leaveAndComeBack(view);

    // Assert
    expect(editorText(await findEditor('說明', section))).toBe('第一版說明');
    expect(storedDraft()).toBe('第一版說明');
  });

  it('離開之後別人改了說明：照樣還原自己的版本，但說明儲存會蓋掉新的說明，並給「看伺服器上的內容」', async () => {
    // Arrange
    const view = await openDescription();
    await startEditing(view, '，我補的一段');
    await view.user.click(within(screen.getByRole('navigation', { name: '主要導覽' })).getByRole('link', { name: '我的工作' }));
    await screen.findByRole('heading', { level: 1, name: '我的工作' });
    serveItem(detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '別人改過的說明'));
    await view.queryClient.invalidateQueries({ queryKey: ['item', 'SMK-7'] });

    // Act
    await view.router.navigate(-1);
    const section = await screen.findByRole('region', { name: '說明' });

    // Assert
    const editor = await findEditor('說明', section);
    expect(editorText(editor)).toBe('原本的說明，我補的一段');
    // 先用手上的舊資料畫出來，重抓到別人改過的說明之後換成警告。
    await waitFor(() => {
      expect(within(section).getByRole('status')).toHaveTextContent(
        '說明在你離開之後被別人改過；這是你上次沒送出的內容，儲存會蓋掉新的說明。',
      );
    });
    const discard = within(section).getByRole('button', { name: '看伺服器上的內容（捨棄還沒儲存的說明）' });
    expect(discard).toHaveTextContent(/^看伺服器上的內容$/);

    // Act
    await view.user.click(discard);

    // Assert
    await waitFor(() => {
      expect(editorText(editor)).toBe('別人改過的說明');
      expect(editor).toHaveFocus();
    });
    expect(within(section).queryByRole('status')).not.toBeInTheDocument();
    expect(sessionStorage.getItem('draft.description.SMK-7')).toBeNull();
  });

  it('打草稿時記下當時伺服器上的說明；伺服器沒變時照一般的說法', async () => {
    // Arrange
    sessionStorage.setItem('draft.description.SMK-7', JSON.stringify({ text: '我的版本', base: '原本的說明' }));

    // Act
    const { section } = await openDescription();

    // Assert
    expect(editorText(await findEditor('說明', section))).toBe('我的版本');
    expect(await within(section).findByRole('status')).toHaveTextContent(/^已還原上次沒送出的內容。/);
    expect(within(section).getByRole('button', { name: '捨棄還沒儲存的說明' })).toHaveTextContent(/^捨棄$/);
  });
});
