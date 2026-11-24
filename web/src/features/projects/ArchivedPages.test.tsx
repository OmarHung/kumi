import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { memberRef, ownerRef, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { serveItemsByQuery } from '../../test/board';
import { serveDatedItems } from '../../test/calendar';
import { makeChangeSet, makeChangeSetDetail, serveChangeSet, serveChangeSets } from '../../test/changesets';
import { deletedItem, serveDeletedItems } from '../../test/deletedItems';
import { agyDetail, exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { serveCycles, serveMilestones, cycleOf } from '../../test/planning';
import { archivedDetail } from '../../test/projects';
import { renderApp } from '../../test/renderApp';
import { simpleTasksBlueprint } from '../../test/blueprints';

const ARCHIVED_TEXT = '這個專案已封存：只能看，不能新增或修改。';
const archived = () => archivedDetail(smkDetail());

function signIn(detail = archived()) {
  signedInAs(exampleOwner, { details: [detail] });
}

describe('封存提示出現在每一種頁面', () => {
  it('專案頁：表格；沒有快速新增、勾選框欄，格子都是唯讀', async () => {
    // Arrange
    signIn();
    serveItems('SMK', [smkItem({ number: 1, title: '首頁設計稿' })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    const table = await screen.findByRole('table', { name: '項目' });
    const row = within(table).getByRole('row', { name: /SMK-1/ });
    expect(within(row).queryAllByRole('button')).toHaveLength(0);
    expect(within(table).queryByRole('checkbox', { name: /選取/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增項目' })).not.toBeInTheDocument();
  });

  it('專案頁：看板；沒有快速新增，卡片沒有「移到…」', async () => {
    // Arrange
    signIn();
    serveItemsByQuery('SMK', [smkItem({ number: 1, title: '首頁設計稿' })]);

    // Act
    renderApp('/projects/SMK?view=board');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT, undefined, { timeout: 3000 })).toBeInTheDocument();
    await screen.findByText('首頁設計稿', undefined, { timeout: 3000 });
    expect(screen.queryByRole('button', { name: /移到/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('form', { name: '新增項目' })).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: '標題' })).not.toBeInTheDocument();
  });

  it('專案頁：行事曆與時間軸', async () => {
    // Arrange
    signIn();
    serveDatedItems('SMK', []);

    // Act
    renderApp('/projects/SMK?view=calendar');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
  });

  it('專案頁：時間軸', async () => {
    // Arrange
    signIn();
    serveDatedItems('SMK', []);

    // Act
    renderApp('/projects/SMK?view=timeline');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
  });

  it('項目頁：有提示；沒有留言框、改類型與刪除，標題與欄位都是唯讀', async () => {
    // Arrange
    signIn();
    const item = detailOf(smkItem({ number: 7, title: '首頁設計稿' }), '第一段說明');
    serveItem(item);
    serveActivity(item.key, []);

    // Act
    renderApp(`/projects/SMK/items/${item.key}`);

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 1, name: /首頁設計稿/ })).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: '新增留言' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /標題：/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /更多動作/ })).not.toBeInTheDocument();
  });

  it('規劃頁：有提示；沒有新增週期與里程碑，每一列沒有編輯的選單', async () => {
    // Arrange
    signIn();
    serveCycles('SMK', [cycleOf('衝刺 1', '2026-11-02', '2026-11-15', { id: 'c1' })]);
    serveMilestones('SMK', []);

    // Act
    renderApp('/projects/SMK/planning');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    const list = await screen.findByRole('list', { name: '週期' });
    expect(within(list).queryAllByRole('button')).toHaveLength(0);
    expect(screen.queryByRole('button', { name: '新增週期' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '新增里程碑' })).not.toBeInTheDocument();
  });

  it('設定頁：有提示，不再說「你的角色不能修改設定」，沒有送出列', async () => {
    // Arrange
    signIn();
    serveChangeSets('SMK', []);

    // Act
    renderApp('/projects/SMK/settings/workflows');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    expect(screen.queryByText(/你的角色不能修改設定/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /預覽變更/ })).not.toBeInTheDocument();
  });

  it('已刪除的項目頁：有提示，沒有「復原」', async () => {
    // Arrange
    signIn(archivedDetail(agyDetail()));
    serveDeletedItems('AGY', [deletedItem(7)]);

    // Act
    renderApp('/projects/AGY/deleted');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    await screen.findByText(/AGY-7/);
    expect(screen.queryByRole('button', { name: /復原/ })).not.toBeInTheDocument();
  });

  it('變更集清單頁：有提示', async () => {
    // Arrange
    signIn();
    serveChangeSets('SMK', [makeChangeSet({ number: 3, summary: '調整流程' })]);

    // Act
    renderApp('/projects/SMK/changesets');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: '#3' })).toBeInTheDocument();
  });

  it('變更集預覽頁：有提示；等待核准的變更集沒有套用、放棄與回去修改', async () => {
    // Arrange
    signIn();
    serveChangeSet(
      'SMK',
      makeChangeSetDetail(simpleTasksBlueprint, { changeSet: makeChangeSet({ number: 3, proposedBy: memberRef }), canApply: true }),
    );

    // Act
    renderApp('/projects/SMK/changesets/3');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '套用' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '放棄' })).not.toBeInTheDocument();
  });

  it('變更集預覽頁：自己提出的等待核准的變更集也不能放棄（伺服器會回 409）', async () => {
    // Arrange
    signIn();
    serveChangeSet('SMK', makeChangeSetDetail(simpleTasksBlueprint, { changeSet: makeChangeSet({ number: 4, proposedBy: ownerRef }), canApply: false }));

    // Act
    renderApp('/projects/SMK/changesets/4');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '放棄' })).not.toBeInTheDocument();
  });
});

describe('封存的提示與「取消封存」', () => {
  it('有 project.manage 的人看得到「取消封存」', async () => {
    // Arrange
    signIn();
    serveChangeSets('SMK', []);

    // Act
    renderApp('/projects/SMK/changesets');

    // Assert
    expect(await screen.findByRole('button', { name: '取消封存' })).toBeInTheDocument();
  });

  it('沒有 project.manage 的人看到提示，沒有「取消封存」', async () => {
    // Arrange
    signIn(archivedDetail(smkDetail({ myPermissions: ['item.read', 'blueprint.read'] })));
    serveChangeSets('SMK', []);

    // Act
    renderApp('/projects/SMK/changesets');

    // Assert
    expect(await screen.findByText(ARCHIVED_TEXT)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '取消封存' })).not.toBeInTheDocument();
  });

  it('沒有封存的專案沒有提示', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveChangeSets('SMK', []);

    // Act
    renderApp('/projects/SMK/changesets');

    // Assert
    await screen.findByRole('heading', { level: 1, name: '變更集' });
    expect(screen.queryByText(ARCHIVED_TEXT)).not.toBeInTheDocument();
  });
});
