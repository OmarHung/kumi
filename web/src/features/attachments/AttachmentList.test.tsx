import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { attachment, attachmentItem, openAttachments, serveAttachments } from '../../test/attachments';
import { created, memberRef, serveActivity } from '../../test/activity';
import { signedInAs } from '../../test/api';
import { ALL_PROJECT_PERMISSIONS, exampleOwner, smkDetail, smkProject } from '../../test/fixtures';
import { detailOf, serveItem, serveItems } from '../../test/items';
import { renderApp } from '../../test/renderApp';

const SPEC = attachment(1, {
  fileName: '規格.pdf',
  contentType: 'application/pdf',
  sizeBytes: Math.round(1.2 * 1024 * 1024),
  uploadedBy: memberRef,
  uploadedAt: '2020-01-02T03:04:00Z',
});
const SCREENSHOT = attachment(2, { fileName: '截圖.png', contentType: 'image/png', sizeBytes: 340 * 1024 });
const LONG_NAME = `${'很長的檔名'.repeat(20)}.docx`;

describe('附件區：清單', () => {
  it('標題是 h2「附件」，旁邊是數量', async () => {
    // Arrange
    const { section } = await openAttachments({ attachments: [SPEC, SCREENSHOT] });

    // Act
    const heading = await within(section).findByRole('heading', { level: 2, name: '附件' });

    // Assert
    expect(heading).toBeInTheDocument();
    expect(await within(section).findByText('2')).toBeInTheDocument();
  });

  it('每一個附件一列：檔名連結指向內容網址、大小、上傳的人、相對時間', async () => {
    // Arrange
    const { section } = await openAttachments({ attachments: [SPEC] });

    // Act
    const list = await within(section).findByRole('list', { name: '附件' });
    const row = within(list).getByRole('listitem');

    // Assert
    expect(within(row).getByRole('link', { name: '規格.pdf' })).toHaveAttribute('href', `/v1/attachments/${SPEC.id}/content`);
    expect(row).toHaveTextContent('1.2 MB');
    expect(row).toHaveTextContent(memberRef.displayName);
    const time = within(row).getByText(/\d{4}\/\d{2}\/\d{2}|前/);
    expect(time.tagName).toBe('TIME');
    expect(time).toHaveAttribute('datetime', '2020-01-02T03:04:00Z');
    expect(time).toHaveAttribute('title', expect.stringMatching(/^2020\/01\/02 /));
  });

  it('圖片在新分頁開；其他類型是下載（download 與 noopener）', async () => {
    // Arrange
    const { section } = await openAttachments({ attachments: [SPEC, SCREENSHOT] });

    // Act
    const image = await within(section).findByRole('link', { name: '截圖.png' });
    const pdf = within(section).getByRole('link', { name: '規格.pdf' });

    // Assert
    expect(image).toHaveAttribute('target', '_blank');
    expect(image).toHaveAttribute('rel', 'noopener');
    expect(image).not.toHaveAttribute('download');
    expect(pdf).toHaveAttribute('download', '規格.pdf');
    expect(pdf).toHaveAttribute('rel', 'noopener');
    expect(pdf).not.toHaveAttribute('target');
  });

  it('SVG 不在瀏覽器裡開：照其他類型下載', async () => {
    // Arrange
    const svg = attachment(3, { fileName: 'logo.svg', contentType: 'image/svg+xml' });
    const { section } = await openAttachments({ attachments: [svg] });

    // Act
    const link = await within(section).findByRole('link', { name: 'logo.svg' });

    // Assert
    expect(link).toHaveAttribute('download', 'logo.svg');
    expect(link).not.toHaveAttribute('target');
  });

  it('很長的檔名完整放在 title（畫面上用省略號）', async () => {
    // Arrange
    const long = attachment(4, { fileName: LONG_NAME });
    const { section } = await openAttachments({ attachments: [long] });

    // Act
    const link = await within(section).findByRole('link', { name: LONG_NAME });

    // Assert
    expect(link).toHaveAttribute('title', LONG_NAME);
  });

  it('不同大小用合適的單位', async () => {
    // Arrange
    const tiny = attachment(5, { fileName: 'a.txt', contentType: 'text/plain', sizeBytes: 300 });
    const { section } = await openAttachments({ attachments: [tiny, SCREENSHOT] });

    // Act
    const rows = await within(section).findAllByRole('listitem');

    // Assert
    expect(rows[0]).toHaveTextContent('300 B');
    expect(rows[1]).toHaveTextContent('340 KB');
  });

  it('沒有附件時寫「還沒有附件」，能上傳的人旁邊有「上傳檔案」', async () => {
    // Arrange
    const { section } = await openAttachments({ attachments: [] });

    // Act
    const empty = await within(section).findByText(/還沒有附件/);

    // Assert
    expect(empty).toBeInTheDocument();
    expect(within(section).getByRole('button', { name: '上傳檔案' })).toBeInTheDocument();
    expect(within(section).queryByRole('list', { name: '附件' })).not.toBeInTheDocument();
  });

  it('位置在說明後面、下層項目與活動前面', async () => {
    // Arrange
    const { section } = await openAttachments({ attachments: [SPEC] });
    await within(section).findByRole('link', { name: '規格.pdf' });

    // Act
    const names = screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent);

    // Assert
    expect(names.indexOf('說明')).toBeGreaterThanOrEqual(0);
    expect(names.indexOf('說明')).toBeLessThan(names.indexOf('附件'));
    expect(names.indexOf('附件')).toBeLessThan(names.indexOf('活動與留言'));
  });

  it('項目面板裡也有附件區（標題是 h3）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [attachmentItem]);
    serveItem(detailOf(attachmentItem));
    serveActivity(attachmentItem.key, [created()]);
    serveAttachments(attachmentItem.key, [SPEC]);
    renderApp('/projects/SMK?item=SMK-7');

    // Act
    const panel = await screen.findByRole('complementary', { name: '項目 SMK-7' });
    const section = await within(panel).findByRole('region', { name: '附件' });

    // Assert
    expect(await within(section).findByRole('link', { name: '規格.pdf' })).toBeInTheDocument();
    expect(within(section).getByRole('heading', { level: 3, name: '附件' })).toBeInTheDocument();
  });

  it('讀不到清單時說明，按「重試」之後顯示清單', async () => {
    // Arrange
    const { section, served, user } = await openAttachments({ attachments: [SPEC], isListFailing: true });
    const retry = await within(section).findByRole('button', { name: '重試' });
    expect(within(section).getByText('附件載入失敗')).toBeInTheDocument();
    served.failList(false);

    // Act
    await user.click(retry);

    // Assert
    expect(await within(section).findByRole('link', { name: '規格.pdf' })).toBeInTheDocument();
  });
});

describe('附件區：權限', () => {
  it('沒有 item.update 的人只看到清單：沒有上傳也沒有刪除', async () => {
    // Arrange
    const { section } = await openAttachments({ attachments: [SPEC], detail: smkDetail({ myPermissions: ['item.read'] }) });

    // Act
    await within(section).findByRole('link', { name: '規格.pdf' });

    // Assert
    expect(within(section).queryByRole('button', { name: '上傳檔案' })).not.toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: /^刪除/ })).not.toBeInTheDocument();
  });

  it('沒有 item.update 且沒有附件時只說「還沒有附件」，不教怎麼上傳', async () => {
    // Arrange
    const { section } = await openAttachments({ detail: smkDetail({ myPermissions: ['item.read'] }) });

    // Act
    const empty = await within(section).findByText(/還沒有附件/);

    // Assert
    expect(empty).toHaveTextContent(/^還沒有附件。$/);
  });

  it('封存的專案沒有上傳也沒有刪除，即使角色有 item.update', async () => {
    // Arrange
    const archived = smkDetail({
      project: { ...smkProject, archivedAt: '2026-11-15T00:00:00Z' },
      myPermissions: ALL_PROJECT_PERMISSIONS,
    });
    const { section } = await openAttachments({ attachments: [SPEC], detail: archived });

    // Act
    await within(section).findByRole('link', { name: '規格.pdf' });

    // Assert
    expect(within(section).queryByRole('button', { name: '上傳檔案' })).not.toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: /^刪除/ })).not.toBeInTheDocument();
  });
});
