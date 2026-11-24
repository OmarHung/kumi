import { useCallback, useEffect, useRef, useState } from 'react';
import type { ProjectBlueprint } from '../../api/blueprint';
import { useQueryClient } from '@tanstack/react-query';
import { PROJECT_ARCHIVED_CODE } from '../../app/queryClient';
import { isApiError } from '../../api/http';
import { queryKeys } from '../../api/queryKeys';
import { importErrorMessage } from './importErrorMessage';
import { importItems } from '../../api/itemImport';
import type { ConvertedRow, RowIssue } from '../../domain/importRows';
import { serverIssuesOf, type ServerIssues } from '../../domain/importServerIssues';
import type { ApiErrorDetail } from '../../api/envelope';

/** 對應改了之後等這麼久才送伺服器檢查（使用者還在連續改的時候不要每改一次就送一次）。 */
export const DRY_RUN_DEBOUNCE_MS = 500;
const IMPORT_INVALID_CODE = 'item.import_invalid';

type Outcome =
  | { readonly kind: 'ok' }
  | { readonly kind: 'rejected'; readonly issues: ServerIssues }
  | { readonly kind: 'failed'; readonly message: string };

interface Result {
  /** 這個結果是替哪一批列檢查的：列換了（改了對應）舊結果就不算數。 */
  readonly rows: readonly ConvertedRow[];
  readonly outcome: Outcome;
}

interface Extra {
  readonly rows: readonly ConvertedRow[];
  readonly issues: ServerIssues;
}

export type CheckStatus = 'idle' | 'checking' | 'done' | 'failed';

export interface ImportCheck {
  readonly status: CheckStatus;
  readonly failureMessage: string | null;
  /** 伺服器指出有問題的列（乾跑加上正式送出被退回的）。 */
  readonly issues: readonly RowIssue[];
  /** 對不到某一列的說明。 */
  readonly general: readonly string[];
  /** 正式送出被伺服器退回（資料在這之間變了）：把問題併進來。 */
  readonly addRejection: (details: readonly ApiErrorDetail[], sentLines: readonly number[]) => void;
  readonly retry: () => void;
}

function toOutcome(error: unknown, rows: readonly ConvertedRow[], blueprint: ProjectBlueprint): Outcome {
  if (isApiError(error) && error.code === IMPORT_INVALID_CODE) {
    return { kind: 'rejected', issues: serverIssuesOf(error.details, rows.map((row) => row.line), blueprint) };
  }
  return { kind: 'failed', message: importErrorMessage(error) };
}

/**
 * 網頁上轉好的列自動送伺服器乾跑（只檢查、不建立）。第一次馬上送，之後改對應再等 DRY_RUN_DEBOUNCE_MS。
 * 沒有可以送的列就不送。
 */
export function useImportCheck(projectKey: string, blueprint: ProjectBlueprint, rows: readonly ConvertedRow[]): ImportCheck {
  const queryClient = useQueryClient();
  const [result, setResult] = useState<Result | null>(null);
  const [extra, setExtra] = useState<Extra | null>(null);
  const [attempt, setAttempt] = useState(0);
  const hasSent = useRef(false);
  useEffect(() => {
    if (rows.length === 0) return undefined;
    let isCancelled = false;
    const timer = window.setTimeout(() => {
      hasSent.current = true;
      importItems(projectKey, { rows: rows.map((row) => row.request), dryRun: true }).then(
        () => {
          if (!isCancelled) setResult({ rows, outcome: { kind: 'ok' } });
        },
        (error: unknown) => {
          // 乾跑不是 mutation，queryClient 的全域處理管不到：別人剛封存了專案時自己讓專案失效，頁面重抓後換成封存的提示。
          if (isApiError(error) && error.code === PROJECT_ARCHIVED_CODE) {
            void queryClient.invalidateQueries({ queryKey: queryKeys.project(projectKey) });
          }
          if (!isCancelled) setResult({ rows, outcome: toOutcome(error, rows, blueprint) });
        },
      );
    }, hasSent.current ? DRY_RUN_DEBOUNCE_MS : 0);
    return () => {
      isCancelled = true;
      window.clearTimeout(timer);
    };
  }, [projectKey, blueprint, rows, attempt, queryClient]);
  const addRejection = useCallback(
    (details: readonly ApiErrorDetail[], sentLines: readonly number[]) => {
      const found = serverIssuesOf(details, sentLines, blueprint);
      setExtra((previous) => {
        const base = previous?.rows === rows ? previous.issues : { issues: [], general: [] };
        return { rows, issues: { issues: [...base.issues, ...found.issues], general: [...base.general, ...found.general] } };
      });
    },
    [blueprint, rows],
  );
  const retry = useCallback(() => {
    setResult(null);
    setAttempt((count) => count + 1);
  }, []);
  return summarize(rows, result, extra, addRejection, retry);
}

function summarize(
  rows: readonly ConvertedRow[],
  result: Result | null,
  extra: Extra | null,
  addRejection: ImportCheck['addRejection'],
  retry: () => void,
): ImportCheck {
  const current = result?.rows === rows ? result.outcome : null;
  const fromServer = current?.kind === 'rejected' ? current.issues : { issues: [], general: [] };
  const fromSubmit = extra?.rows === rows ? extra.issues : { issues: [], general: [] };
  const status: CheckStatus = rows.length === 0 ? 'idle' : current === null ? 'checking' : current.kind === 'failed' ? 'failed' : 'done';
  return {
    status,
    failureMessage: current?.kind === 'failed' ? current.message : null,
    issues: [...fromServer.issues, ...fromSubmit.issues],
    general: [...fromServer.general, ...fromSubmit.general],
    addRejection,
    retry,
  };
}
