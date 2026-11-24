import { useRef, useState, type DragEvent } from 'react';
import type { ProjectBlueprint } from '../../api/blueprint';
import { IMPORT_MAX_ROWS } from '../../api/itemImport';
import { Button } from '../../components/Button/Button';
import { Notice } from '../../components/Notice/Notice';
import { formatFileSize } from '../../domain/importSummary';
import { TEMPLATE_FILE_NAME, templateCsv } from '../../domain/importTemplate';
import { downloadTextFile } from './downloadTextFile';
import { IMPORT_MAX_BYTES, readImportFile, type LoadedImportFile } from './readImportFile';
import styles from './ItemImport.module.css';

interface ImportFilePickerProps {
  readonly blueprint: ProjectBlueprint;
  readonly canManage: boolean;
  readonly loaded: LoadedImportFile | null;
  readonly onLoaded: (file: LoadedImportFile | null) => void;
}

const ACCEPT = '.csv,.tsv,.txt,text/csv,text/tab-separated-values';
const MAX_MB = IMPORT_MAX_BYTES / (1024 * 1024);

/** 讀檔：成功交給 onLoaded，不行就留下說明（讀不了的檔案不留著上一份）。 */
function useAcceptFile(onLoaded: (file: LoadedImportFile | null) => void) {
  const [error, setError] = useState<string | null>(null);
  const [isReading, setIsReading] = useState(false);
  // state 要下一次 render 才變：連續拖兩個檔案時用 ref 擋，讀取中不收新的，結果才不會互相覆蓋。
  const isBusy = useRef(false);
  async function accept(file: File | undefined) {
    if (file === undefined || isBusy.current) return;
    isBusy.current = true;
    setError(null);
    setIsReading(true);
    const outcome = await readImportFile(file);
    isBusy.current = false;
    setIsReading(false);
    if (outcome.ok) {
      onLoaded(outcome.file);
    } else {
      onLoaded(null);
      setError(outcome.message);
    }
  }
  return { error, isReading, accept };
}

interface DropZoneProps {
  readonly blueprint: ProjectBlueprint;
  readonly canManage: boolean;
  readonly isLoaded: boolean;
  readonly isReading: boolean;
  readonly onFile: (file: File | undefined) => void;
}

function DropActions({ blueprint, canManage, isLoaded, isReading, onFile }: DropZoneProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  return (
    <>
      <p className={styles.dropText}>把 CSV 或 TSV 檔拖到這裡，或</p>
      <div className={styles.dropActions}>
        <Button
          variant="primary"
          aria-disabled={isReading || undefined}
          onClick={() => {
            if (isReading) return;
            inputRef.current?.click();
          }}
        >
          {isReading ? '讀取中…' : isLoaded ? '換一個檔案' : '選擇檔案'}
        </Button>
        <Button
          variant="ghost"
          onClick={() => {
            downloadTextFile(TEMPLATE_FILE_NAME, templateCsv(blueprint, canManage));
          }}
        >
          下載範本
        </Button>
      </div>
      <input
        ref={inputRef}
        type="file"
        hidden
        accept={ACCEPT}
        aria-label="匯入的檔案"
        onChange={(event) => {
          onFile(event.target.files?.[0]);
          event.target.value = '';
        }}
      />
    </>
  );
}

function DropZone({ blueprint, canManage, isLoaded, isReading, onFile }: DropZoneProps) {
  const [isDragging, setIsDragging] = useState(false);
  function onDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setIsDragging(false);
    onFile(event.dataTransfer.files[0]);
  }
  return (
    <div
      className={styles.drop}
      data-content-start
      data-dragging={isDragging || undefined}
      onDragOver={(event) => {
        event.preventDefault();
        setIsDragging(true);
      }}
      onDragLeave={(event) => {
        // 游標進到裡面的子元素也會觸發 dragleave：還在拖曳區裡就不取消高亮，免得閃。
        if (event.relatedTarget instanceof Node && event.currentTarget.contains(event.relatedTarget)) return;
        setIsDragging(false);
      }}
      onDrop={onDrop}
    >
      <DropActions blueprint={blueprint} canManage={canManage} isLoaded={isLoaded} isReading={isReading} onFile={onFile} />
      <p className={styles.hint}>
        最大 {MAX_MB} MB、{IMPORT_MAX_ROWS.toLocaleString('en-US')} 列；第一列是標題。Excel 檔請先另存成 CSV。
      </p>
    </div>
  );
}

/** 選檔案：按鈕或拖進來，旁邊可以下載範本。讀不了的檔案在這裡說明原因。 */
export function ImportFilePicker({ blueprint, canManage, loaded, onLoaded }: ImportFilePickerProps) {
  const { error, isReading, accept } = useAcceptFile(onLoaded);
  return (
    <section aria-labelledby="import-file-heading" className={styles.section}>
      <h2 id="import-file-heading" className={styles.heading}>選擇檔案</h2>
      <DropZone
        blueprint={blueprint}
        canManage={canManage}
        isLoaded={loaded !== null}
        isReading={isReading}
        onFile={(file) => {
          void accept(file);
        }}
      />
      {error !== null && <Notice tone="danger">{error}</Notice>}
      {loaded !== null && <LoadedSummary file={loaded} />}
    </section>
  );
}

function LoadedSummary({ file }: { readonly file: LoadedImportFile }) {
  return (
    <>
      <p className={styles.fileLine}>
        <span className={styles.fileName}>{file.name}</span>
        <span className={styles.muted}>
          {formatFileSize(file.size)}・{file.rows.length.toLocaleString('en-US')} 列資料
        </span>
      </p>
      {file.encoding === 'big5' && (
        <Notice tone="info">以 Big5 讀取。如果文字有亂碼，請在 Excel 另存成「CSV UTF-8」再選一次。</Notice>
      )}
      {file.encoding.startsWith('utf-16') && (
        <Notice tone="info">以 UTF-16 讀取（Excel 的「Unicode 文字」）。如果文字有亂碼，請在 Excel 另存成「CSV UTF-8」再選一次。</Notice>
      )}
    </>
  );
}
