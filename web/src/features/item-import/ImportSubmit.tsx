import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useId } from 'react';
import { useNavigate } from 'react-router';
import { isApiError } from '../../api/http';
import type { ApiErrorDetail } from '../../api/envelope';
import { importItems } from '../../api/itemImport';
import { queryKeys } from '../../api/queryKeys';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import type { ConvertedRow } from '../../domain/importRows';
import { importButtonLabel, importedNotice } from '../../domain/importSummary';
import { projectPageLink } from '../projects/projectViewPreference';
import { importErrorMessage } from './importErrorMessage';
import type { ImportCheck } from './useImportCheck';
import styles from './ItemImport.module.css';

const IMPORT_INVALID_CODE = 'item.import_invalid';

interface ImportSubmitProps {
  readonly projectKey: string;
  readonly importable: readonly ConvertedRow[];
  /** 略過的列數（網頁或伺服器檢查出問題的）。 */
  readonly skipped: number;
  readonly check: ImportCheck;
}

/** 送出匯入：成功回到專案頁帶提示；伺服器退回（資料在這之間變了）就留在這一頁，問題併進檢查的結果。 */
function useSubmitImport(projectKey: string, check: ImportCheck) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  return useMutation({
    mutationFn: (rows: readonly ConvertedRow[]) => importItems(projectKey, { rows: rows.map((row) => row.request), dryRun: false }),
    onSuccess: async (result) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.project(projectKey) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.myWork }),
      ]);
      await navigate(projectPageLink(projectKey), { state: { notice: importedNotice(result.count, result.createdKeys) } });
    },
    onError: (error, rows) => {
      if (isApiError(error) && error.code === IMPORT_INVALID_CODE) {
        const details: readonly ApiErrorDetail[] = error.details;
        check.addRejection(details, rows.map((row) => row.line));
      }
    },
  });
}

function disabledReason(importable: number, check: ImportCheck): string | null {
  if (check.status === 'failed') return '伺服器的檢查沒有完成，請重新檢查。';
  if (check.status === 'checking') return null;
  return importable === 0 ? '沒有可以匯入的列：每一列都有問題。請修正檔案或調整對應。' : null;
}

export function ImportSubmit({ projectKey, importable, skipped, check }: ImportSubmitProps) {
  const submit = useSubmitImport(projectKey, check);
  const reason = disabledReason(importable.length, check);
  const reasonId = useId();
  const isRejected = submit.isError && isApiError(submit.error) && submit.error.code === IMPORT_INVALID_CODE;
  const isBlocked = importable.length === 0 || check.status !== 'done';
  return (
    <div className={styles.submit}>
      {check.status === 'failed' && (
        <Notice tone="danger" actions={<Button size="sm" onClick={check.retry}>重新檢查</Button>}>
          {check.failureMessage}
        </Notice>
      )}
      {isRejected && <Notice tone="warning">伺服器重新檢查後有列不能匯入，一個項目都還沒建立。問題已經列在上面。</Notice>}
      {submit.isError && !isRejected && <Notice tone="danger">{importErrorMessage(submit.error)}</Notice>}
      <div className={styles.submitRow}>
        <Button
          variant="primary"
          aria-disabled={isBlocked || submit.isPending || undefined}
          aria-describedby={reason !== null && check.status !== 'failed' ? reasonId : undefined}
          onClick={() => {
            // aria-disabled 不會擋點擊：送出中（焦點還在按鈕上）與不能匯入時都由這裡擋。
            if (isBlocked || submit.isPending) return;
            submit.mutate(importable);
          }}
        >
          {submit.isPending ? '匯入中…' : check.status === 'checking' ? '檢查中…' : importButtonLabel(importable.length, skipped)}
        </Button>
        {reason !== null && check.status !== 'failed' && <p id={reasonId} className={styles.hint}>{reason}</p>}
      </div>
    </div>
  );
}
