import { screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { commented, created, serveActivity, serveComment } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { descriptionRevisionOf, detailOf, serveItem, servePatch } from '../../test/items';
import { describedBy, serveDescriptionTemplates, serveProjectDescription } from '../../test/projectDescription';
import { renderApp } from '../../test/renderApp';
import { setEditorMode } from '../../lib/editorMode';

// 編輯方式是 Markdown 原文時，呼叫端（說明、留言、專案說明）打字、儲存、送出的就是原文，不經過編輯器的轉換。
beforeEach(() => {
  setEditorMode('markdown');
});

describe('Markdown 原文模式下的呼叫端', () => {
  it('項目的說明：打什麼存什麼（`* 一` 不被改成 `- 一`），儲存之後回到閱讀', async () => {
    // Arrange
    const item = detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '* 舊的');
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(item);
    serveActivity(item.key, []);
    const patches = servePatch(item);
    const { user } = renderApp(`/projects/SMK/items/${item.key}`);
    const section = await screen.findByRole('region', { name: '說明' });

    // Act
    await user.click(within(section).getByRole('button', { name: '編輯說明' }));
    const box = await within(section).findByRole('textbox', { name: '說明' });
    expect(box).toHaveValue('* 舊的');
    await user.click(box);
    await user.keyboard('{Control>}{End}{/Control}{Enter}* 新的');
    await user.click(within(section).getByRole('button', { name: '儲存' }));

    // Assert
    expect(await within(section).findByRole('button', { name: '編輯說明' })).toBeInTheDocument();
    expect(patches.map((request) => request.body)).toEqual([{ description: '* 舊的\n* 新的', expectedDescriptionRevision: descriptionRevisionOf('* 舊的') }]);
  });

  it('留言：Ctrl+Enter 送出的是打的原文，送出後清空、焦點留在框裡', async () => {
    // Arrange
    const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItem(task);
    const activity = serveActivity(task.key, [created(), commented('先前的留言')]);
    const requests = serveComment(task.key, { timeline: activity });
    const { user } = renderApp('/projects/SMK/items/SMK-7');
    const section = await screen.findByRole('region', { name: '活動與留言' });
    const box = await within(section).findByRole('textbox', { name: '留言' });

    // Act
    await user.click(box);
    await user.keyboard('**重點**{Enter}* 一');
    await user.keyboard('{Control>}{Enter}{/Control}');

    // Assert
    expect(await within(section).findAllByText('一')).not.toHaveLength(0);
    expect(requests.map((request) => request.body)).toEqual([{ body: '**重點**\n* 一' }]);
    expect(box).toHaveValue('');
    expect(box).toHaveFocus();
  });

  it('專案說明：儲存送出的是原文', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveDescriptionTemplates();
    const backend = serveProjectDescription('SMK', describedBy('舊的主機'));
    const { user } = renderApp('/projects/SMK/info');
    await user.click(await screen.findByRole('button', { name: '編輯專案說明' }));
    const box = await screen.findByRole('textbox', { name: '說明' });

    // Act
    await user.click(box);
    await user.keyboard('{Control>}{End}{/Control}{Enter}* 新的主機');
    await user.click(screen.getByRole('button', { name: '儲存' }));

    // Assert
    expect(await screen.findByText('新的主機')).toBeInTheDocument();
    expect(backend.saved[0]?.body).toEqual({ text: '舊的主機\n* 新的主機' });
  });
});
