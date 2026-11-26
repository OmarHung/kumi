import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { http } from 'msw';
import { describe, expect, it } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { CurrentUserProvider } from '../../auth/CurrentUser';
import { ok } from '../../test/api';
import { exampleMember, exampleOwner } from '../../test/fixtures';
import { memberPerson } from '../../test/people';
import { server } from '../../test/server';
import { usePersonTitles } from './usePersonTitles';
import type { SessionUser } from '../../api/types';

function Probe({ isEnabled }: { readonly isEnabled?: boolean }) {
  const titles = usePersonTitles(isEnabled);
  return <p>職稱：{titles.get(exampleMember.personId) ?? '無'}</p>;
}

function renderProbe(user: SessionUser, isEnabled?: boolean) {
  let requests = 0;
  server.use(
    http.get('/v1/people', () => {
      requests += 1;
      return ok([{ ...memberPerson, jobTitle: '前端工程師' }]);
    }),
  );
  render(
    <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
      <CurrentUserProvider user={user}>
        <Probe isEnabled={isEnabled} />
      </CurrentUserProvider>
    </QueryClientProvider>,
  );
  return { requests: () => requests };
}

describe('usePersonTitles', () => {
  it('成員讀人員清單，拿到職稱', async () => {
    // Arrange / Act
    const { requests } = renderProbe(exampleOwner);

    // Assert
    expect(await screen.findByText('職稱：前端工程師')).toBeInTheDocument();
    expect(requests()).toBe(1);
  });

  it('訪客不發 /v1/people（讀不到，會是 403），沒有職稱', async () => {
    // Arrange / Act
    const { requests } = renderProbe({ ...exampleMember, role: 'guest' });
    await new Promise((resolve) => setTimeout(resolve, 50));

    // Assert
    await waitFor(() => {
      expect(screen.getByText('職稱：無')).toBeInTheDocument();
    });
    expect(requests()).toBe(0);
  });

  it('isEnabled 是 false 時不發請求', async () => {
    // Arrange / Act
    const { requests } = renderProbe(exampleOwner, false);
    await new Promise((resolve) => setTimeout(resolve, 50));

    // Assert
    expect(screen.getByText('職稱：無')).toBeInTheDocument();
    expect(requests()).toBe(0);
  });
});
