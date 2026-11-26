import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner } from '../../test/fixtures';
import { createFakeLiveConnection } from '../../test/liveConnection';
import { overviewOf, serveOverview, webOverview } from '../../test/overview';
import { renderApp } from '../../test/renderApp';

describe('專案總覽的即時更新', () => {
  it('收到專案的 items 推送後重抓，別人剛改的數字不用重新整理就換上', async () => {
    // Arrange
    signedInAs(exampleOwner);
    serveOverview([webOverview]);
    const live = createFakeLiveConnection();
    renderApp('/overview', { liveConnection: () => live });
    await screen.findByRole('link', { name: 'WEB 已過期 2 個' });

    // Act
    serveOverview([overviewOf('WEB', '官網改版', { ...webOverview, overdue: 5 })]);
    live.emitItems({ projectKey: 'WEB', itemKeys: ['WEB-3'] });

    // Assert
    expect(await screen.findByRole('link', { name: 'WEB 已過期 5 個' })).toBeInTheDocument();
  });
});
