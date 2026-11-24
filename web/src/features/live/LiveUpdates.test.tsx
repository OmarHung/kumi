import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail, smkItem } from '../../test/fixtures';
import { serveItems } from '../../test/items';
import { createFakeLiveConnection, type FakeLiveConnection } from '../../test/liveConnection';
import { renderApp } from '../../test/renderApp';

describe('登入後的即時更新', () => {
  it('收到 items 訊息之後，畫面上的清單不重新整理就換成別人改的標題', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '舊的標題' })]);
    const live = createFakeLiveConnection();
    renderApp('/projects/SMK', { liveConnection: () => live });
    await screen.findByText('舊的標題');

    // Act
    serveItems('SMK', [smkItem({ number: 1, title: '別人改的標題' })]);
    live.emitItems({ projectKey: 'SMK', itemKeys: ['SMK-1'] });

    // Assert
    expect(await screen.findByText('別人改的標題')).toBeInTheDocument();
    expect(live.starts()).toBe(1);
  });

  it('離開登入後的畫面（登出、工作階段失效）時關掉連線', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '舊的標題' })]);
    const live = createFakeLiveConnection();
    const { unmount } = renderApp('/projects/SMK', { liveConnection: () => live });
    await screen.findByText('舊的標題');

    // Act
    unmount();

    // Assert
    expect(live.stops()).toBe(1);
  });

  it('沒有提供連線時不連線（測試與沒有即時更新的環境）', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '舊的標題' })]);

    // Act
    renderApp('/projects/SMK');

    // Assert
    expect(await screen.findByText('舊的標題')).toBeInTheDocument();
  });

  it('StrictMode（掛上、清理、再掛上）之後只留一條連線', async () => {
    // Arrange
    signedInAs(exampleOwner, { details: [smkDetail()] });
    serveItems('SMK', [smkItem({ number: 1, title: '舊的標題' })]);
    const created: FakeLiveConnection[] = [];
    const factory = () => {
      const connection = createFakeLiveConnection();
      created.push(connection);
      return connection;
    };

    // Act
    renderApp('/projects/SMK', { strict: true, liveConnection: factory });
    await screen.findByText('舊的標題');

    // Assert：第一次掛上的那條已經關掉，只有一條還開著
    expect(created.length).toBeGreaterThanOrEqual(2);
    expect(created.filter((connection) => connection.stops() === 0)).toHaveLength(1);
  });
});
