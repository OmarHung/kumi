import { useMemo, useRef, useState } from 'react';
import type { ProjectDetail } from '../../api/types';
import { LoadingState } from '../../components/StatusViews/StatusViews';
import { PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { convertRows, type ConvertedRow } from '../../domain/importRows';
import { assignTarget, autoMapColumns, importTargets, isMapped, type ColumnMapping, type ImportTarget } from '../../domain/importTargets';
import { useProjectPageLink } from '../projects/projectViewPreference';
import { ImportCheckResult } from './ImportCheckResult';
import { ImportColumnMapping } from './ImportColumnMapping';
import { ImportFilePicker } from './ImportFilePicker';
import { ImportSubmit } from './ImportSubmit';
import type { LoadedImportFile } from './readImportFile';
import { useImportCheck } from './useImportCheck';
import { useImportLookups } from './useImportLookups';
import styles from './ItemImport.module.css';

const NO_ROWS: readonly ConvertedRow[] = [];
const STATE_TARGET: ImportTarget = 'state';

/** 檔案、對應、預設類型：換檔案時對應重新自動算。 */
function useImportSelection(detail: ProjectDetail, canManage: boolean) {
  const targets = useMemo(() => importTargets(detail.blueprint), [detail.blueprint]);
  const [loaded, setLoaded] = useState<LoadedImportFile | null>(null);
  const [mapping, setMapping] = useState<ColumnMapping>([]);
  const [defaultType, setDefaultType] = useState(detail.blueprint.itemTypes[0]?.key ?? '');
  const load = (file: LoadedImportFile | null) => {
    setLoaded(file);
    // 沒有 project.manage 的人不能匯入狀態，所以不自動對應。
    setMapping(file === null ? [] : autoMapColumns(file.header.cells, targets, canManage ? new Set() : new Set([STATE_TARGET])));
  };
  const map = (index: number, target: ImportTarget | null) => {
    setMapping((current) => assignTarget(current, index, target));
  };
  return { targets, loaded, mapping, defaultType, setDefaultType, load, map };
}

/** 網頁上把每一列轉好（要等成員、週期、里程碑讀完，而且有標題欄）。 */
function useConversion(detail: ProjectDetail, loaded: LoadedImportFile | null, mapping: ColumnMapping, defaultType: string) {
  const { isReady, members, cycles, milestones } = useImportLookups(detail);
  const hasTitle = isMapped(mapping, 'title');
  const conversion = useMemo(
    () =>
      loaded !== null && hasTitle && isReady
        ? convertRows(loaded.rows, mapping, { blueprint: detail.blueprint, members, cycles, milestones, defaultType })
        : null,
    [loaded, hasTitle, isReady, members, cycles, milestones, mapping, detail.blueprint, defaultType],
  );
  return { conversion, isLoadingLookups: hasTitle && !isReady };
}

/** 匯入項目：選檔案、對應欄位、檢查、匯入，同一頁由上往下，前一步完成下一步才出現。 */
export function ItemImportContent({ detail }: { readonly detail: ProjectDetail }) {
  const projectKey = detail.project.key;
  const canManage = detail.myPermissions.includes('project.manage');
  const selection = useImportSelection(detail, canManage);
  const { loaded, mapping, defaultType } = selection;
  const { conversion, isLoadingLookups } = useConversion(detail, loaded, mapping, defaultType);
  const check = useImportCheck(projectKey, detail.blueprint, conversion?.rows ?? NO_ROWS);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const crumbs = [{ label: projectKey, mono: true, to: useProjectPageLink(projectKey) }];
  const flagged = new Set(check.issues.map((issue) => issue.line));
  const importable = (conversion?.rows ?? NO_ROWS).filter((row) => !flagged.has(row.line));
  return (
    <PageLayout variant="reading" bar={<PageBar title="匯入項目" crumbs={crumbs} headingRef={headingRef} />}>
      <div className={styles.page}>
        <ImportFilePicker blueprint={detail.blueprint} canManage={canManage} loaded={loaded} onLoaded={selection.load} />
        {loaded !== null && (
          <ImportColumnMapping
            blueprint={detail.blueprint}
            file={loaded}
            targets={selection.targets}
            mapping={mapping}
            canManage={canManage}
            defaultType={defaultType}
            onMap={selection.map}
            onDefaultType={selection.setDefaultType}
          />
        )}
        {loaded !== null && isLoadingLookups && <LoadingState label="正在載入成員與規劃…" />}
        {loaded !== null && conversion !== null && (
          <section aria-labelledby="import-check-heading" className={styles.section}>
            <h2 id="import-check-heading" className={styles.heading}>檢查結果</h2>
            <ImportCheckResult
              blueprint={detail.blueprint}
              mapping={mapping}
              totalRows={loaded.rows.length}
              importable={importable}
              issues={[...conversion.issues, ...check.issues]}
              general={check.general}
              isChecking={check.status === 'checking'}
            />
            <ImportSubmit projectKey={projectKey} importable={importable} skipped={loaded.rows.length - importable.length} check={check} />
          </section>
        )}
      </div>
    </PageLayout>
  );
}
