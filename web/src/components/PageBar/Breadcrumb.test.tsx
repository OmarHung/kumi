import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderInRouter } from '../../test/renderInRouter';
import { PageBar } from './PageBar';

describe('Breadcrumb 的目前位置', () => {
  it('頁列上有標題時，目前的頁面是標題：位置裡沒有連結的那一段不標 aria-current', () => {
    // Arrange & Act
    renderInRouter(<PageBar title="接案測試" crumbs={[{ label: 'AGY', mono: true }]} />);

    // Assert
    const location = screen.getByRole('navigation', { name: '位置' });
    expect(within(location).getByText('AGY')).not.toHaveAttribute('aria-current');
    expect(screen.getByRole('heading', { level: 1, name: '接案測試' })).toBeInTheDocument();
  });

  it('標題不在頁列時，位置的最後一段才是目前的頁面', () => {
    // Arrange & Act
    renderInRouter(
      <PageBar title="變更集 #3" titleInBar={false} crumbs={[{ label: '變更集', to: '/changesets' }, { label: '#3' }]} />,
    );

    // Assert
    const location = screen.getByRole('navigation', { name: '位置' });
    expect(within(location).getByText('#3')).toHaveAttribute('aria-current', 'page');
    expect(within(location).getByRole('link', { name: '變更集' })).not.toHaveAttribute('aria-current');
  });
});
