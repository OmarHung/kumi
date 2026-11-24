import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import type { ProjectDetail } from '../../api/types';
import { memberAsEditor, ownerAsAdmin, smkDetail } from '../../test/fixtures';
import { abilitiesOf, ProjectTableProvider } from '../item-table/ProjectTableContext';
import { useMentionNames } from './useMentionNames';

/** 每次渲染讀 holder 裡現在的專案：換掉 holder.detail 再 rerender，就像專案重抓了一次。 */
function renderNames(holder: { detail: ProjectDetail }) {
  return renderHook(() => useMentionNames(), {
    wrapper: ({ children }: { readonly children: ReactNode }) => (
      <ProjectTableProvider
        value={{ detail: holder.detail, can: abilitiesOf(holder.detail), todayDate: '2026-11-18', onOpenMembers: () => undefined }}
      >
        {children}
      </ProjectTableProvider>
    ),
  });
}

describe('useMentionNames', () => {
  it('專案重抓之後成員的內容沒變（新的陣列、新的物件）：回傳同一個 Map', () => {
    // Arrange
    const holder = { detail: smkDetail({ members: [ownerAsAdmin, memberAsEditor] }) };
    const { result, rerender } = renderNames(holder);
    const before = result.current;

    // Act
    holder.detail = smkDetail({ members: [{ ...ownerAsAdmin }, { ...memberAsEditor }] });
    rerender();

    // Assert
    expect(result.current).toBe(before);
  });

  it('名字變了：換成新的 Map', () => {
    // Arrange
    const holder = { detail: smkDetail({ members: [ownerAsAdmin] }) };
    const { result, rerender } = renderNames(holder);
    const before = result.current;

    // Act
    holder.detail = smkDetail({ members: [{ ...ownerAsAdmin, displayName: '新名字' }] });
    rerender();

    // Assert
    expect(result.current).not.toBe(before);
    expect(result.current.get(ownerAsAdmin.personId)).toBe('新名字');
  });
});
