import { useQuery } from '@tanstack/react-query';
import { apiRequest } from './http';
import { queryKeys } from './queryKeys';

/** kumi 執行檔的平台（internal/HANDOFF-cli-downloads.md 第 1 節），順序和伺服器回的一樣。 */
export type CliPlatform = 'macos-arm64' | 'macos-x64' | 'windows-x64';

export interface CliPackage {
  readonly platform: CliPlatform;
  readonly fileName: string;
  readonly sizeBytes: number;
  readonly sha256: string;
}

/** GET /v1/cli：沒有提供下載時 version 是 null、packages 是空的（200，不是錯誤）。 */
export interface CliCatalog {
  readonly version: string | null;
  readonly packages: readonly CliPackage[];
}

const CLI_PATH = '/v1/cli';

export function cliDownloadPath(platform: CliPlatform): string {
  return `${CLI_PATH}/download/${platform}`;
}

export function getCliCatalog(signal?: AbortSignal): Promise<CliCatalog> {
  return apiRequest<CliCatalog>(CLI_PATH, { signal });
}

/** 映像檔裡的檔案不會變，不必一直重讀。 */
export function useCliCatalog() {
  return useQuery({
    queryKey: queryKeys.cliCatalog,
    queryFn: ({ signal }) => getCliCatalog(signal),
    staleTime: Infinity,
  });
}
