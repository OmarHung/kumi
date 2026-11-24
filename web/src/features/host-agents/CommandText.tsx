import { Fragment, type ReactNode } from 'react';
import { COMMAND_NAME } from '../../config/product';
import styles from './HostAgents.module.css';

/**
 * 主機上要執行的指令（docker compose exec -it ai-worker kumi host pair／login <引擎>）。worker 回報的說明與來源的狀態
 * 是伺服器給的一句話，指令夾在中間（src/PmTool.Cli/Host/HostEngineProbe.cs），網頁認出來另外畫：不改 CLI 的文字，舊版的 worker 也一樣。
 */
const COMMAND_PATTERN = new RegExp(`docker compose exec -it ai-worker ${COMMAND_NAME} host (?:pair|login [a-z-]+)`, 'g');

/** 指令：等寬字，只在空白處換行（每個字各自不斷行，不會從 ai-worker 的連字號斷開）；複製出來就是原本的指令。 */
function InlineCommand({ command }: { readonly command: string }) {
  return (
    <code className={styles.command}>
      {command.split(' ').map((word, index) => (
        // 指令的字可能重複，位置才是它的身分（清單不會變動）。
        <Fragment key={index}>
          {index > 0 && ' '}
          <span className={styles.commandWord}>{word}</span>
        </Fragment>
      ))}
    </code>
  );
}

/** 一句說明：裡面的主機指令換成 InlineCommand，其他的字照原樣。 */
export function TextWithCommands({ text }: { readonly text: string }) {
  const parts: ReactNode[] = [];
  let rest = 0;
  for (const match of text.matchAll(COMMAND_PATTERN)) {
    if (match.index > rest) parts.push(text.slice(rest, match.index));
    parts.push(<InlineCommand key={match.index} command={match[0]} />);
    rest = match.index + match[0].length;
  }
  if (rest < text.length) parts.push(text.slice(rest));
  return <>{parts}</>;
}
