import { QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { KnowledgeAttachment } from '../../../api/knowledgeTypes';
import { queryKeys } from '../../../api/queryKeys';
import { createQueryClient } from '../../../app/queryClient';
import { ownerRef } from '../../../test/activity';
import { aFile, installFakeUploads, type FakeUpload } from '../../../test/attachments';
import type { UploadTarget } from '../../attachments/useEditorUploads';
import { NO_PAGE_UPLOAD_PERMISSION, usePageEditorUploads } from './usePageEditorUploads';

const uploaded: KnowledgeAttachment = {
  id: '0190a100-0000-7000-8000-000000000042',
  pageId: '0190a100-0000-7000-8000-000000000007',
  fileName: '架構圖.png',
  contentType: 'image/png',
  sizeBytes: 2048,
  sha256: 'b'.repeat(64),
  uploadedBy: ownerRef,
  uploadedAt: '2026-11-25T08:00:00Z',
};

let fakes: readonly FakeUpload[];

function aTarget() {
  const replacePlaceholder = vi.fn<UploadTarget['replacePlaceholder']>(() => true);
  const insertPlaceholders = vi.fn<UploadTarget['insertPlaceholders']>();
  const target: UploadTarget = { insertPlaceholders, replacePlaceholder };
  return { target, replacePlaceholder, insertPlaceholders };
}

function hookWith(canUpload: boolean) {
  const client = createQueryClient({ queries: { retry: false } });
  const wrapper = ({ children }: { readonly children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  return renderHook(() => usePageEditorUploads({ spaceKey: 'ENG', pageNumber: 7, canUpload, value: '', onChange: vi.fn(), maxLength: 1000 }), { wrapper });
}

beforeEach(() => {
  fakes = installFakeUploads();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('usePageEditorUploads', () => {
  it('傳到這一頁的附件網址，傳完把記號換成知識庫附件的參照', async () => {
    // Arrange
    const { result } = hookWith(true);
    const { target, replacePlaceholder } = aTarget();
    act(() => {
      result.current.binding.attach(target);
    });

    // Act
    act(() => {
      result.current.addFiles([aFile('架構圖.png', 2048, 'image/png')]);
    });
    await waitFor(() => {
      expect(fakes).toHaveLength(1);
    });
    act(() => {
      fakes[0]?.respond(201, uploaded);
    });

    // Assert
    expect(fakes[0]?.method).toBe('POST');
    expect(fakes[0]?.url).toBe('/v1/knowledge/spaces/ENG/pages/7/attachments');
    expect(fakes[0]?.file?.name).toBe('架構圖.png');
    await waitFor(() => {
      expect(replacePlaceholder).toHaveBeenCalledWith('![上傳中：架構圖.png…]()', `![架構圖.png](attachment:${uploaded.id})`);
    });
  });

  it('傳完讓這一頁的附件清單失效，別頁的不動', async () => {
    // Arrange
    const client = createQueryClient({ queries: { retry: false } });
    const wrapper = ({ children }: { readonly children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
    const pageKey = queryKeys.knowledgePageAttachments('ENG', 7);
    const otherKey = queryKeys.knowledgePageAttachments('ENG', 8);
    client.setQueryData(pageKey, []);
    client.setQueryData(otherKey, []);
    const { result } = renderHook(() => usePageEditorUploads({ spaceKey: 'ENG', pageNumber: 7, canUpload: true, value: '', onChange: vi.fn(), maxLength: 1000 }), { wrapper });
    act(() => {
      result.current.binding.attach(aTarget().target);
    });

    // Act
    act(() => {
      result.current.addFiles([aFile('報告.pdf')]);
    });
    await waitFor(() => {
      expect(fakes).toHaveLength(1);
    });
    act(() => {
      fakes[0]?.respond(201, { ...uploaded, fileName: '報告.pdf', contentType: 'application/pdf' });
    });

    // Assert
    await waitFor(() => {
      expect(client.getQueryState(pageKey)?.isInvalidated).toBe(true);
    });
    expect(client.getQueryState(otherKey)?.isInvalidated).toBe(false);
  });

  it('不能上傳時不送出請求，說明原因', () => {
    // Arrange
    const { result } = hookWith(false);
    act(() => {
      result.current.binding.attach(aTarget().target);
    });

    // Act
    act(() => {
      result.current.addFiles([aFile('報告.pdf')]);
    });

    // Assert
    expect(result.current.canUpload).toBe(false);
    expect(result.current.problems).toEqual([NO_PAGE_UPLOAD_PERMISSION]);
    expect(NO_PAGE_UPLOAD_PERMISSION).toBe('你不能在這一頁上傳檔案');
    expect(fakes).toHaveLength(0);
  });

  it('超過 25 MB 的檔案上傳前就擋下', () => {
    // Arrange
    const { result } = hookWith(true);
    act(() => {
      result.current.binding.attach(aTarget().target);
    });

    // Act
    act(() => {
      result.current.addFiles([aFile('大檔.zip', 26 * 1024 * 1024)]);
    });

    // Assert
    expect(result.current.problems).toEqual(['超過 25 MB：大檔.zip']);
    expect(fakes).toHaveLength(0);
  });
});
