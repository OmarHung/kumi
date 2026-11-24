import { screen, waitFor, within } from '@testing-library/react';
import { delay, http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Permission } from '../../api/blueprint';
import type { Activity } from '../../api/types';
import { commented, created, deletedComment, editedComment, memberRef, ownerRef, serveActivity, serveCommentEditing } from '../../test/activity';
import { failure, signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem, smkProject } from '../../test/fixtures';
import { detailOf, serveItem } from '../../test/items';
import { renderApp } from '../../test/renderApp';
import { clearEditor, editorText, findEditor, typeInEditor } from '../../test/richText';
import { server } from '../../test/server';
import { draftKey } from '../../lib/draftStorage';
import { queryKeys } from '../../api/queryKeys';

const task = detailOf(smkItem({ number: 7, title: '首頁設計稿' }));
const WRITER: readonly Permission[] = ['item.read', 'comment.create', 'blueprint.read'];
const MORE = '範例使用者 的留言的更多動作';
const EDIT_LABEL = '編輯 範例使用者 的留言';

async function openComments(history: readonly Activity[], permissions: readonly Permission[] = WRITER) {
  signedInAs(exampleOwner, { details: [smkDetail({ project: smkProject, myPermissions: permissions })] });
  serveItem(task);
  const timeline = serveActivity(task.key, history);
  const served = serveCommentEditing(task.key, timeline, { viewer: { personId: exampleOwner.personId, displayName: exampleOwner.displayName } });
  const view = renderApp('/projects/SMK/items/SMK-7');
  const section = await screen.findByRole('region', { name: '活動與留言' });
  await within(section).findByRole('list', { name: '留言' });
  return { ...view, section, timeline, served };
}

type Opened = Awaited<ReturnType<typeof openComments>>;

async function choose(opened: Pick<Opened, 'user' | 'section'>, name: string) {
  await opened.user.click(within(opened.section).getByRole('button', { name: MORE }));
  await opened.user.click(await screen.findByRole('menuitem', { name }));
}

function draftKeyOf(section: HTMLElement): string {
  return draftKey('comment-edit', task.key, section.querySelector<HTMLElement>('[data-activity-id]')?.dataset.activityId ?? '');
}

/** 時間軸那一份「別處」的狀態：把第一則留言換成指定的樣子，再讓活動失效（即時更新就是這樣進來的）。 */
async function changeElsewhere(opened: Opened, change: (target: Activity) => Activity) {
  const [head, target] = opened.timeline.all();
  if (head === undefined || target === undefined) throw new Error('沒有留言');
  opened.timeline.set([head, change(target)]);
  await opened.queryClient.invalidateQueries({ queryKey: queryKeys.activity(task.key) });
}

function statusText(section: HTMLElement): string {
  return within(section)
    .getAllByRole('status')
    .map((node) => node.textContent)
    .join('|');
}

describe('編輯中刪除', () => {
  it('自己從選單刪除：草稿清掉，復原之後編輯器不會自己打開；再按「編輯」是原文、沒有已還原的提示', async () => {
    // Arrange
    const opened = await openComments([created(), commented('原本的內容')]);
    await choose(opened, '編輯');
    await typeInEditor(opened.user, await findEditor(EDIT_LABEL, opened.section), '，改到一半');
    const key = draftKeyOf(opened.section);
    expect(sessionStorage.getItem(key)).toBe('原本的內容，改到一半');
    await opened.user.click(within(opened.section).getByRole('button', { name: MORE }));
    expect(screen.queryByRole('menuitem', { name: '編輯' })).not.toBeInTheDocument();
    await opened.user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    const dialog = await screen.findByRole('dialog', { name: '刪除這則留言？' });

    // Act
    await opened.user.click(within(dialog).getByRole('button', { name: '刪除' }));
    await opened.user.click(await within(opened.section).findByRole('button', { name: '復原 範例使用者 的留言' }));

    // Assert
    expect(await within(opened.section).findByText('原本的內容')).toBeInTheDocument();
    expect(within(opened.section).queryByRole('form', { name: EDIT_LABEL })).not.toBeInTheDocument();
    expect(sessionStorage.getItem(key)).toBeNull();
    await choose(opened, '編輯');
    const editor = await findEditor(EDIT_LABEL, opened.section);
    expect(editorText(editor)).toBe('原本的內容');
    expect(within(opened.section).queryByText('已還原上次沒送出的內容。')).not.toBeInTheDocument();
  });

  it('被別人刪掉：編輯器收起來但草稿留著；復原之後編輯器不自己打開，按「編輯」才還原草稿並提示', async () => {
    // Arrange
    const opened = await openComments([created(), commented('原本的內容')]);
    await choose(opened, '編輯');
    await typeInEditor(opened.user, await findEditor(EDIT_LABEL, opened.section), '，改到一半');
    const key = draftKeyOf(opened.section);
    const original = opened.timeline.all()[1];
    if (original === undefined) throw new Error('沒有留言');

    // Act：別人刪掉，之後又復原。
    await changeElsewhere(opened, (target) => ({ ...target, comment: null, deletedAt: '2026-11-15T10:00:00Z', deletedBy: memberRef }));
    expect(await within(opened.section).findByText('這則留言已由 範例成員 刪除。')).toBeInTheDocument();
    expect(within(opened.section).queryByRole('form', { name: EDIT_LABEL })).not.toBeInTheDocument();
    expect(sessionStorage.getItem(key)).toBe('原本的內容，改到一半');
    await changeElsewhere(opened, () => original);

    // Assert
    expect(await within(opened.section).findByText('原本的內容')).toBeInTheDocument();
    expect(within(opened.section).queryByRole('form', { name: EDIT_LABEL })).not.toBeInTheDocument();
    await choose(opened, '編輯');
    const editor = await findEditor(EDIT_LABEL, opened.section);
    expect(editorText(editor)).toBe('原本的內容，改到一半');
    expect(await within(opened.section).findByText('已還原上次沒送出的內容。')).toBeInTheDocument();
  });

  it('確認框開著時留言被別人刪掉：框收起來、佔位拿到焦點；復原之後框不會自己再開', async () => {
    // Arrange
    const opened = await openComments([created(), commented('我寫的')]);
    const original = opened.timeline.all()[1];
    if (original === undefined) throw new Error('沒有留言');
    await opened.user.click(within(opened.section).getByRole('button', { name: MORE }));
    await opened.user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    await screen.findByRole('dialog', { name: '刪除這則留言？' });

    // Act
    await changeElsewhere(opened, (target) => ({ ...target, comment: null, deletedAt: '2026-11-15T10:00:00Z', deletedBy: ownerRef }));

    // Assert
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(opened.served.deletes).toHaveLength(0);
    await waitFor(() => {
      expect(within(opened.section).getByText('這則留言已刪除。').closest('[data-comment-card]')).toHaveFocus();
    });
    await changeElsewhere(opened, () => original);
    expect(await within(opened.section).findByText('我寫的')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('編輯的草稿：開著編輯器的判斷', () => {
  it('草稿等於原文：不算有草稿，編輯器不自己打開', async () => {
    // Arrange
    const target = commented('原本的內容');
    sessionStorage.setItem(draftKey('comment-edit', task.key, target.id), '原本的內容');

    // Act
    const { section } = await openComments([created(), target]);

    // Assert
    expect(within(section).queryByRole('form', { name: EDIT_LABEL })).not.toBeInTheDocument();
  });

  it('草稿只有空白：不算有草稿', async () => {
    // Arrange
    const target = commented('原本的內容');
    sessionStorage.setItem(draftKey('comment-edit', task.key, target.id), '   ');

    // Act
    const { section } = await openComments([created(), target]);

    // Assert
    expect(within(section).queryByRole('form', { name: EDIT_LABEL })).not.toBeInTheDocument();
  });
});

describe('儲存時留言已被刪除（409）', () => {
  async function saveIntoDeleted() {
    const opened = await openComments([created(), commented('原本的內容')]);
    const original = opened.timeline.all()[1];
    if (original === undefined) throw new Error('沒有留言');
    await choose(opened, '編輯');
    await typeInEditor(opened.user, await findEditor(EDIT_LABEL, opened.section), '新增');
    // 伺服器那邊已經刪除了（儲存回 409），時間軸那邊還沒換，畫面還不知道。
    server.use(http.patch(`/v1/items/${task.key}/comments/:id`, () => failure(409, 'comment.deleted', '這則留言已經刪除，先復原才能編輯。')));
    await opened.user.click(within(opened.section).getByRole('button', { name: '儲存' }));
    return { opened, original };
  }

  it('焦點放到那則提示，不掉到頁面本體；草稿留著', async () => {
    // Arrange & Act
    const { opened } = await saveIntoDeleted();

    // Assert
    const alert = await within(opened.section).findByRole('alert');
    expect(alert).toHaveTextContent('這則留言已經被刪除了。');
    await waitFor(() => {
      expect(alert).toHaveFocus();
    });
    expect(sessionStorage.getItem(draftKeyOf(opened.section))).toBe('原本的內容新增');
  });

  it('重抓成佔位之後提示不見；別人復原之後提示不會再冒出來', async () => {
    // Arrange
    const { opened, original } = await saveIntoDeleted();
    await within(opened.section).findByRole('alert');

    // Act
    await changeElsewhere(opened, (target) => ({ ...target, comment: null, deletedAt: '2026-11-15T10:00:00Z', deletedBy: ownerRef }));
    expect(await within(opened.section).findByText('這則留言已刪除。')).toBeInTheDocument();
    expect(within(opened.section).queryByText('這則留言已經被刪除了。')).not.toBeInTheDocument();
    await changeElsewhere(opened, () => original);

    // Assert
    expect(await within(opened.section).findByText('原本的內容')).toBeInTheDocument();
    expect(within(opened.section).queryByText('這則留言已經被刪除了。')).not.toBeInTheDocument();
  });
});

describe('標頭', () => {
  it('沒有可做的事（別人的留言）時不畫空的外層：時間就是最後一個元素，右緣和自己的留言對齊', async () => {
    // Arrange & Act
    const { section } = await openComments([created(), commented('別人寫的', { actor: memberRef })]);

    // Assert
    const stamp = within(section).getByText('別人寫的').closest('li')?.querySelector('time')?.parentElement;
    const line = stamp?.parentElement;
    expect(line?.lastElementChild).toBe(stamp);
  });

  it('刪除與復原前後：留言標頭那一行都套同一個固定高度的 class', async () => {
    // Arrange
    const opened = await openComments([created(), commented('我寫的')]);
    const lineOf = () => within(opened.section).getByText('範例使用者', { selector: 'span' }).closest('p');
    const before = lineOf()?.className;

    // Act
    await opened.user.click(within(opened.section).getByRole('button', { name: MORE }));
    await opened.user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    await opened.user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '刪除' }));
    await within(opened.section).findByText('這則留言已刪除。');

    // Assert
    expect(before).toContain('commentLine');
    expect(lineOf()?.className).toBe(before);
  });
});

describe('報讀', () => {
  it('儲存、刪除、復原成功各唸一句', async () => {
    // Arrange
    const opened = await openComments([created(), commented('原本的內容')]);

    // Act & Assert：儲存
    await choose(opened, '編輯');
    await typeInEditor(opened.user, await findEditor(EDIT_LABEL, opened.section), '加');
    await opened.user.click(within(opened.section).getByRole('button', { name: '儲存' }));
    await waitFor(() => {
      expect(statusText(opened.section)).toContain('已儲存留言');
    });

    // 刪除
    await opened.user.click(within(opened.section).getByRole('button', { name: MORE }));
    await opened.user.click(await screen.findByRole('menuitem', { name: '刪除' }));
    await opened.user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: '刪除' }));
    await waitFor(() => {
      expect(statusText(opened.section)).toContain('已刪除留言');
    });

    // 復原
    await opened.user.click(await within(opened.section).findByRole('button', { name: '復原 範例使用者 的留言' }));
    await waitFor(() => {
      expect(statusText(opened.section)).toContain('已復原留言');
    });
  });

  it('復原送出中：名稱與 aria-busy 讓報讀軟體聽得到進行中', async () => {
    // Arrange
    const { section, user } = await openComments([created(), deletedComment({ actor: ownerRef })]);
    server.use(
      // 一直不回應：只量「進行中」的樣子。給固定的延遲的話，負載重時回應會在 findByRole 與檢查之間到達，按鈕已經不在進行中。
      http.post(`/v1/items/${task.key}/comments/:id/restore`, async () => {
        await delay('infinite');
        return HttpResponse.json({ success: true, data: commented('回來了'), error: null });
      }),
    );

    // Act
    await user.click(await within(section).findByRole('button', { name: '復原 範例使用者 的留言' }));

    // Assert
    const busy = await within(section).findByRole('button', { name: '復原 範例使用者 的留言（復原中）' });
    expect(busy).toHaveAttribute('aria-busy', 'true');
  });
});

describe('名稱', () => {
  it('「已編輯」的名稱帶作者；編輯表單與編輯器的名稱帶作者', async () => {
    // Arrange
    const opened = await openComments([created(), editedComment('改過的')]);

    // Act
    const mark = within(opened.section).getByRole('button', { name: '已編輯（查看 範例使用者 的留言的版本）' });
    await choose(opened, '編輯');

    // Assert
    expect(mark).toBeInTheDocument();
    expect(await within(opened.section).findByRole('form', { name: EDIT_LABEL })).toBeInTheDocument();
    expect(await findEditor(EDIT_LABEL, opened.section)).toBeInTheDocument();
  });
});

describe('編輯的失敗與版本', () => {
  async function failOnSave(respond: () => Response) {
    const opened = await openComments([created(), commented('原本的內容')]);
    server.use(http.patch(`/v1/items/${task.key}/comments/:id`, respond));
    await choose(opened, '編輯');
    const editor = await findEditor(EDIT_LABEL, opened.section);
    await typeInEditor(opened.user, editor, '新增');
    await opened.user.click(within(opened.section).getByRole('button', { name: '儲存' }));
    return { opened, editor };
  }

  it('編輯次數太多（comment.too_many_edits）：伺服器的訊息放在編輯器裡，編輯器留著', async () => {
    // Arrange & Act
    const { opened, editor } = await failOnSave(() => failure(409, 'comment.too_many_edits', '這則留言已經編輯太多次了。'));

    // Assert
    expect(await within(opened.section).findByText('這則留言已經編輯太多次了。')).toBeInTheDocument();
    expect(editorText(editor)).toBe('原本的內容新增');
  });

  it('不是作者（403 comment.not_author）：訊息放在編輯器裡', async () => {
    // Arrange & Act
    const { opened } = await failOnSave(() => failure(403, 'comment.not_author', '只能編輯自己的留言。'));

    // Assert
    expect(await within(opened.section).findByText('只能編輯自己的留言。')).toBeInTheDocument();
    expect(within(opened.section).getByRole('form', { name: EDIT_LABEL })).toBeInTheDocument();
  });

  it('網路斷了：說連不上伺服器，內容留著、可以再按儲存', async () => {
    // Arrange & Act
    const { opened, editor } = await failOnSave(() => HttpResponse.error());

    // Assert
    expect(await within(opened.section).findByText('連不上伺服器，請確認網路連線後再試。')).toBeInTheDocument();
    expect(editorText(editor)).toBe('原本的內容新增');
    expect(within(opened.section).getByRole('button', { name: '儲存' })).toBeEnabled();
  });

  it('編輯成功之後再開版本：看得到新的版本（先前讀過的版本清單已經失效）', async () => {
    // Arrange：先開過一次，快取裡是只有一版的舊清單。
    const opened = await openComments([created(), editedComment('第一版')]);
    const markName = '已編輯（查看 範例使用者 的留言的版本）';
    await opened.user.click(within(opened.section).getByRole('button', { name: markName }));
    const first = await screen.findByRole('dialog', { name: '留言的版本' });
    expect(await within(first).findAllByRole('listitem')).toHaveLength(1);
    await opened.user.click(within(first).getByRole('button', { name: '關閉' }));
    await choose(opened, '編輯');
    await clearEditor(opened.user, await findEditor(EDIT_LABEL, opened.section));
    await typeInEditor(opened.user, await findEditor(EDIT_LABEL, opened.section), '第二版');
    await opened.user.click(within(opened.section).getByRole('button', { name: '儲存' }));
    await within(opened.section).findByText('第二版');

    // Act
    await opened.user.click(within(opened.section).getByRole('button', { name: markName }));

    // Assert
    const dialog = await screen.findByRole('dialog', { name: '留言的版本' });
    await waitFor(() => {
      expect(within(dialog).getAllByRole('listitem')).toHaveLength(2);
    });
    expect(within(dialog).getAllByRole('listitem')[0]).toHaveTextContent('第二版');
  });
});
