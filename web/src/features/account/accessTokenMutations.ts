import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { isApiError } from '../../api/http';
import { createAccessToken, revokeAccessToken } from '../../api/account';
import { queryKeys } from '../../api/queryKeys';
import type { AccessToken, CreateAccessTokenRequest } from '../../api/types';

function refetchTokens(queryClient: QueryClient): Promise<void> {
  return queryClient.invalidateQueries({ queryKey: queryKeys.accessTokens });
}

/**
 * 建立。回應裡的明文只交給 onSecret（對話框自己的 state），不回傳：mutation 的結果會留在 MutationCache 裡一陣子，
 * 明文不能放在任何快取（internal/HANDOFF-pat-cli.md 第 6 節）。清單只收沒有明文的那一把，成功之後重抓，順序由伺服器決定。
 */
export function useCreateAccessToken(onSecret: (secret: string) => void) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (request: CreateAccessTokenRequest): Promise<AccessToken> => {
      const { token, secret } = await createAccessToken(request);
      onSecret(secret);
      return token;
    },
    onSuccess: () => refetchTokens(queryClient),
  });
}

const NOT_FOUND = 404;

export interface RevokeResult {
  readonly tokenId: string;
  /** 伺服器說找不到（別的分頁先撤了）：目的已經達成，當成功，只是提示不同。 */
  readonly wasAlreadyGone: boolean;
}

async function revokeOrAlreadyGone(tokenId: string): Promise<RevokeResult> {
  try {
    const revoked = await revokeAccessToken(tokenId);
    return { tokenId: revoked.tokenId, wasAlreadyGone: false };
  } catch (error) {
    if (isApiError(error) && error.status === NOT_FOUND) return { tokenId, wasAlreadyGone: true };
    throw error;
  }
}

/** 撤銷。成功時那一把從清單拿掉；404（已經不在了）也當成功，並重抓讓畫面和伺服器對上；其他失敗同樣重抓。 */
export function useRevokeAccessToken() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: revokeOrAlreadyGone,
    onSuccess: (result) => {
      queryClient.setQueryData<readonly AccessToken[]>(queryKeys.accessTokens, (tokens) =>
        tokens?.filter((token) => token.tokenId !== result.tokenId),
      );
      if (result.wasAlreadyGone) void refetchTokens(queryClient);
    },
    onError: () => {
      void refetchTokens(queryClient);
    },
  });
}
