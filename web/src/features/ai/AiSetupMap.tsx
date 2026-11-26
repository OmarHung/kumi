import { Link } from 'react-router';
import { useCurrentUser } from '../../auth/CurrentUser';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { Toggletip } from '../../components/Toggletip/Toggletip';
import { AI_SETUP_COLUMNS, canOpenAiSetup, managedByText, visibleAiSetupRows, type AiSetupCell, type AiSetupPage } from './aiSetupCells';
import styles from './AiSetupMap.module.css';

/** 標題照列出的格數寫：一般成員只看得到自己的兩種（全組織那一列不列）。 */
const titleOf = (cellCount: number): string => `AI 的${cellCount === 4 ? '四' : '兩'}種接法`;
const DESCRIPTION = '看兩件事：誰能用，和用什麼登入。費用一律算在登入的那個帳號。';
/** 只剩一列（一般成員只看得到自己的兩種）時沒有「誰能用」可比，只講登入方式。 */
const SINGLE_ROW_DESCRIPTION = '差別在用什麼登入。費用一律算在登入的那個帳號。';
const ADVICE =
  '建議：全組織共用先開組織的 API 金鑰（快、不排隊、不用維護主機）；想讓大家用 Claude Code／Codex 又不想各自安裝，再加主機 AI 代理。已經有 Claude、ChatGPT 訂閱的人，用本機代理最省。';

interface CellProps {
  readonly cell: AiSetupCell;
  readonly page: AiSetupPage;
}

/** 名稱旁邊的兩個說明提示：好處（勾）與代價（警告），滑過、鍵盤移到上面或點一下才浮出那一句（每一頁都有，不佔版面）。 */
function Tradeoffs({ cell }: { readonly cell: AiSetupCell }) {
  return (
    <span className={styles.tradeoffs}>
      <Toggletip icon="success" tone="success" label="好處" text={cell.pros} />
      <Toggletip icon="warning" tone="warning" label="代價" text={cell.cons} />
    </span>
  );
}

function CellBody({ cell, page }: CellProps) {
  const user = useCurrentUser();
  const isCurrent = cell.page === page;
  const managedBy = managedByText(cell);
  return (
    <td className={styles.cell} data-current={isCurrent || undefined}>
      <span className={styles.head}>
        {canOpenAiSetup(cell, user) ? (
          <Link className={styles.label} to={cell.href} aria-current={isCurrent ? 'page' : undefined}>
            {cell.label}
          </Link>
        ) : (
          <span className={styles.label}>{cell.label}</span>
        )}
        <Tradeoffs cell={cell} />
      </span>
      <span className={styles.detail}>{cell.detail}</span>
      {!canOpenAiSetup(cell, user) && managedBy !== null && <span className={styles.detail}>{managedBy}</span>}
    </td>
  );
}

/** 說明：怎麼看這張表；完整版再加一句選擇的建議（四格各自的好處與代價寫在格子裡）。 */
function MapNote({ isDetailed, isSingleRow }: { readonly isDetailed: boolean; readonly isSingleRow: boolean }) {
  return (
    <>
      <p>{isSingleRow ? SINGLE_ROW_DESCRIPTION : DESCRIPTION}</p>
      {isDetailed && <p>{ADVICE}</p>}
    </>
  );
}

/**
 * 三頁 AI 助理設定最上面的對照（docs/design/08-interaction.md 8.3「AI 的四種接法」），整塊滿寬（右欄放不下四格）。
 * 每一格的好處與代價在名稱旁邊的兩個說明提示裡（每一頁都有）；isDetailed（AI 助理設定頁：管理員在這裡決定開哪幾種）多一句選擇的建議。：兩列（全組織共用、只有自己）乘兩欄（API 金鑰、Claude Code／Codex）。
 * 目前這一頁的格子用選取的底（和次導覽的目前所在同一套），其他格子是連到那一頁的連結；看不到的頁不給連結，寫由誰設定。
 * 用真的表格：列與欄的標題是 th，報讀軟體唸得出「全組織共用、API 金鑰：組織的 API 金鑰」。
 */
export function AiSetupMap({ page, isDetailed = false }: { readonly page: AiSetupPage; readonly isDetailed?: boolean }) {
  const rows = visibleAiSetupRows(useCurrentUser());
  // 只有一列時不畫列標題那一欄：「誰能用」只有一個答案，不必比。
  const isSingleRow = rows.length === 1;
  return (
    <SettingsSection title={titleOf(rows.length * 2)} description={<MapNote isDetailed={isDetailed} isSingleRow={isSingleRow} />} isContentStart isFullWidth>
      <table className={styles.table}>
        <thead>
          <tr>
            {/* 角落是列標題那一欄的欄標題：報讀軟體不會唸出一個沒有名稱的格子。 */}
            {!isSingleRow && (
              <th scope="col" className={styles.corner}>
                <span className="visually-hidden">誰能用</span>
              </th>
            )}
            {AI_SETUP_COLUMNS.map((column) => (
              <th key={column} scope="col" className={styles.columnHeader}>
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.label}>
              {!isSingleRow && (
                <th scope="row" className={styles.rowHeader}>
                  {row.label}
                </th>
              )}
              {row.cells.map((cell) => (
                <CellBody key={cell.kind} cell={cell} page={page} />
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </SettingsSection>
  );
}
