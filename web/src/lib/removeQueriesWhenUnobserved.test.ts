import { QueryClient, QueryObserver } from '@tanstack/react-query';
import { describe, expect, it } from 'vitest';
import { removeQueriesWhenUnobserved } from './removeQueriesWhenUnobserved';

function clientWith(...keys: readonly (readonly string[])[]): QueryClient {
  const queryClient = new QueryClient();
  for (const key of keys) {
    queryClient.setQueryData(key, { value: key.join('/') });
  }
  return queryClient;
}

describe('等沒有人在看才丟掉查詢', () => {
  it('沒有元件在看：馬上丟掉這個前綴底下的每一份', () => {
    // Arrange
    const queryClient = clientWith(['project', 'AGY'], ['project', 'AGY', 'items'], ['project', 'SMK']);

    // Act
    removeQueriesWhenUnobserved(queryClient, ['project', 'AGY']);

    // Assert
    expect(queryClient.getQueryData(['project', 'AGY'])).toBeUndefined();
    expect(queryClient.getQueryData(['project', 'AGY', 'items'])).toBeUndefined();
    expect(queryClient.getQueryData(['project', 'SMK'])).toEqual({ value: 'project/SMK' });
  });

  it('還有元件在看：等最後一個不看了（頁面卸下）才丟掉，之前不重讀', () => {
    // Arrange
    const queryClient = clientWith(['project', 'AGY'], ['project', 'AGY', 'items']);
    let reads = 0;
    const observe = (key: readonly string[]) =>
      new QueryObserver(queryClient, {
        queryKey: key,
        queryFn: () => {
          reads += 1;
          return { value: 'again' };
        },
        staleTime: Infinity,
      }).subscribe(() => undefined);
    const stopDetail = observe(['project', 'AGY']);
    const stopItems = observe(['project', 'AGY', 'items']);

    // Act
    removeQueriesWhenUnobserved(queryClient, ['project', 'AGY']);
    const whileObserved = queryClient.getQueryData(['project', 'AGY']);
    stopDetail();
    const afterFirst = queryClient.getQueryData(['project', 'AGY']);
    stopItems();

    // Assert
    expect(whileObserved).toEqual({ value: 'project/AGY' });
    expect(afterFirst).toEqual({ value: 'project/AGY' });
    expect(queryClient.getQueryData(['project', 'AGY'])).toBeUndefined();
    expect(queryClient.getQueryData(['project', 'AGY', 'items'])).toBeUndefined();
    expect(reads).toBe(0);
  });
});
