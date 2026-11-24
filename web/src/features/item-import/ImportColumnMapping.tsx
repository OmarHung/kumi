import { useMemo } from 'react';
import type { ProjectBlueprint } from '../../api/blueprint';
import { SelectField } from '../../components/Field/Field';
import { Notice } from '../../components/Notice/Notice';
import { Select, type SelectOption } from '../../components/Select/Select';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { isMapped, type ColumnMapping, type ImportTarget, type ImportTargetOption } from '../../domain/importTargets';
import type { LoadedImportFile } from './readImportFile';
import styles from './ItemImport.module.css';

const NOT_IMPORTED = '';
const MANAGER_ONLY_REASON = '只有專案管理員能匯入狀態';

interface ImportColumnMappingProps {
  readonly blueprint: ProjectBlueprint;
  readonly file: LoadedImportFile;
  readonly targets: readonly ImportTargetOption[];
  readonly mapping: ColumnMapping;
  readonly canManage: boolean;
  readonly defaultType: string;
  readonly onMap: (columnIndex: number, target: ImportTarget | null) => void;
  readonly onDefaultType: (typeKey: string) => void;
}

function optionsFor(targets: readonly ImportTargetOption[], canManage: boolean): readonly SelectOption[] {
  return [
    { value: NOT_IMPORTED, label: '不匯入' },
    ...targets.map((target, index): SelectOption => ({
      value: target.id,
      label: target.label,
      startsGroup: index === 0,
      disabledReason: target.id === 'state' && !canManage ? MANAGER_ONLY_REASON : undefined,
    })),
  ];
}

interface MappingRowProps {
  readonly header: string;
  readonly index: number;
  readonly sample: string;
  readonly value: ImportTarget | null;
  readonly options: readonly SelectOption[];
  readonly onMap: (columnIndex: number, target: ImportTarget | null) => void;
}

function MappingRow({ header, index, sample, value, options, onMap }: MappingRowProps) {
  const name = header.trim() === '' ? `第 ${String(index + 1)} 欄` : header;
  return (
    <tr>
      <th scope="row" className={styles.headerCell}>{header.trim() === '' ? `（${name}）` : header}</th>
      <td className={styles.sampleCell}>{sample}</td>
      <td className={styles.selectCell}>
        <Select
          size="sm"
          aria-label={`「${name}」要匯入成`}
          value={value ?? NOT_IMPORTED}
          options={options}
          onChange={(next) => {
            onMap(index, next === NOT_IMPORTED ? null : (next as ImportTarget));
          }}
        />
      </td>
    </tr>
  );
}

/** 對應欄位：每個檔案的欄位一列（標題、第一筆的值、要匯入成什麼）。沒有對應到類型時多一個「全部匯入成」。 */
export function ImportColumnMapping(props: ImportColumnMappingProps) {
  const { blueprint, file, targets, mapping, canManage, defaultType, onMap, onDefaultType } = props;
  const options = useMemo(() => optionsFor(targets, canManage), [targets, canManage]);
  const typeOptions = blueprint.itemTypes.map((type): SelectOption => ({ value: type.key, label: type.name }));
  const firstRow = file.rows[0]?.cells ?? [];
  return (
    <section aria-labelledby="import-mapping-heading" className={styles.section}>
      <h2 id="import-mapping-heading" className={styles.heading}>對應欄位</h2>
      <TableScroll label="欄位對應" layout="inline">
        <table className={tableStyles.table}>
          <caption className="visually-hidden">欄位對應</caption>
          <thead>
            <tr>
              <th scope="col">檔案的欄位</th>
              <th scope="col">第一筆的值</th>
              <th scope="col">匯入成</th>
            </tr>
          </thead>
          <tbody>
            {file.header.cells.map((header, index) => (
              <MappingRow
                key={`${String(index)}-${header}`}
                header={header}
                index={index}
                sample={firstRow[index] ?? ''}
                value={mapping[index] ?? null}
                options={options}
                onMap={onMap}
              />
            ))}
          </tbody>
        </table>
      </TableScroll>
      {!canManage && <p className={styles.hint}>{MANAGER_ONLY_REASON}。</p>}
      {!isMapped(mapping, 'title') && (
        <Notice tone="warning">要有一欄匯入成「標題」才能繼續。請在上面選一欄。</Notice>
      )}
      {!isMapped(mapping, 'type') && (
        <SelectField
          label="全部匯入成"
          hint="檔案裡沒有類型的欄位時，每一列都用這個類型。"
          value={defaultType}
          options={typeOptions}
          onChange={onDefaultType}
          className={styles.typeField}
        />
      )}
    </section>
  );
}
