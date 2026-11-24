import { QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createQueryClient } from '../../app/queryClient';
import { aFile, installFakeUploads, serveAttachments, type FakeUpload } from '../../test/attachments';
import { smkDetail } from '../../test/fixtures';
import { abilitiesOf, ProjectTableProvider } from '../item-table/ProjectTableContext';
import type { UploadTarget } from './useEditorUploads';
import { useItemEditorUploads } from './useItemEditorUploads';

const detail = smkDetail();

function wrapper({ children }: { readonly children: ReactNode }) {
  return (
    <QueryClientProvider client={createQueryClient({ queries: { retry: false } })}>
      <ProjectTableProvider value={{ detail, can: abilitiesOf(detail), todayDate: '2026-11-20', onOpenMembers: () => undefined }}>{children}</ProjectTableProvider>
    </QueryClientProvider>
  );
}

function aTarget() {
  const insertPlaceholders = vi.fn<UploadTarget['insertPlaceholders']>();
  const target: UploadTarget = { insertPlaceholders, replacePlaceholder: () => true };
  return { target, insertPlaceholders };
}

let fakes: readonly FakeUpload[];

beforeEach(() => {
  fakes = installFakeUploads();
  serveAttachments('SMK-7', []);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('useEditorUploads 沒有註冊目標時', () => {
  it('編輯器還在下載時選的檔案先記著，不改文字也不上傳；目標註冊時才照常加入', async () => {
    // Arrange
    const onChange = vi.fn();
    const { result } = renderHook(() => useItemEditorUploads({ itemKey: 'SMK-7', value: '', onChange, maxLength: 1000 }), { wrapper });
    const { target, insertPlaceholders } = aTarget();

    // Act
    act(() => {
      result.current.addFiles([aFile('報告.pdf')]);
    });

    // Assert
    expect(onChange).not.toHaveBeenCalled();
    expect(insertPlaceholders).not.toHaveBeenCalled();
    expect(fakes).toHaveLength(0);

    // Act
    act(() => {
      result.current.binding.attach(target);
    });

    // Assert
    expect(insertPlaceholders).toHaveBeenCalledWith([{ placeholder: '[上傳中：報告.pdf…]()', name: '報告.pdf', isImage: false }]);
    await waitFor(() => {
      expect(fakes).toHaveLength(1);
    });
    expect(onChange).not.toHaveBeenCalled();
  });

  it('註冊之後再註冊（編輯器重掛）不會重複加入先前記著的檔案', () => {
    // Arrange
    const { result } = renderHook(() => useItemEditorUploads({ itemKey: 'SMK-7', value: '', onChange: vi.fn(), maxLength: 1000 }), { wrapper });
    const first = aTarget();
    const second = aTarget();
    act(() => {
      result.current.addFiles([aFile('報告.pdf')]);
    });

    // Act
    act(() => {
      result.current.binding.attach(first.target);
      result.current.binding.attach(null);
      result.current.binding.attach(second.target);
    });

    // Assert
    expect(first.insertPlaceholders).toHaveBeenCalledTimes(1);
    expect(second.insertPlaceholders).not.toHaveBeenCalled();
  });
});
