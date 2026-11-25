import { useState } from 'react';
import { Link } from 'react-router';
import { errorMessage } from '../../api/http';
import { Button } from '../../components/Button/Button';
import { Dialog } from '../../components/Dialog/Dialog';
import { Notice } from '../../components/Notice/Notice';
import { COMMAND_NAME } from '../../config/product';
import { useCreateAccessToken } from './accessTokenMutations';
import { CopyBlock } from './ConnectAgentSection';
import {
  ENABLE_COMMAND,
  LOCAL_AGENT_SCOPES,
  LOCAL_AGENT_TOKEN_DAYS,
  OLD_TOKEN_NOTE,
  START_COMMAND,
  localAgentTokenName,
  loginCommand,
  pasteLoginCommand,
} from './localAgentText';
import { TokenSecretView } from './TokenSecretView';
import accountStyles from './AccountPage.module.css';
import styles from './LocalAgent.module.css';

/**
 * 「建立本機代理用的權杖」：一鍵建一把剛好夠用的（agent:run、blueprint:read、blueprint:write、items:read、90 天）。
 * 明文只交給對話框自己的 state，關上就卸下，和一般的建立權杖同一個畫面（TokenSecretView），不進任何快取。
 * 建立中按鈕只標 aria-disabled、焦點留著；失敗的原因寫在按鈕旁邊那一行（永遠掛著，不推動版面）。
 */
function CreateAgentToken() {
  const [secret, setSecret] = useState<string | null>(null);
  const create = useCreateAccessToken(setSecret);
  return (
    <div className={styles.tokenRow}>
      <Button
        aria-disabled={create.isPending || undefined}
        onClick={() => {
          if (create.isPending) return;
          create.mutate({ name: localAgentTokenName(new Date()), scopes: LOCAL_AGENT_SCOPES, expiresInDays: LOCAL_AGENT_TOKEN_DAYS });
        }}
      >
        {create.isPending ? '建立中…' : '建立本機代理用的權杖'}
      </Button>
      <Notice tone="danger">{create.isError ? errorMessage(create.error) : null}</Notice>
      {secret !== null && (
        <Dialog
          title="本機代理用的權杖已建立"
          description="複製之後再關上這個視窗，下一步要貼到終端機。"
          onClose={() => {
            setSecret(null);
          }}
        >
          <TokenSecretView
            secret={secret}
            onDone={() => {
              setSecret(null);
            }}
          />
        </Dialog>
      )}
    </div>
  );
}

const SETUP_PATH = '/account/integrations#setup';

function WizardLink() {
  return <Link to={SETUP_PATH}>設定精靈</Link>;
}

/** 沒辦法開瀏覽器（例如透過 SSH）時的做法：建一把權杖，用 `kumi agent login --paste` 貼上。 */
function PasteFallback({ origin }: { readonly origin: string }) {
  return (
    <>
      <CreateAgentToken />
      <CopyBlock label="貼上權杖登入的指令" text={pasteLoginCommand(origin)} wrap />
      <p className={accountStyles.note}>{OLD_TOKEN_NOTE}</p>
    </>
  );
}

/**
 * 接上電腦：最省事的是在那台電腦執行「命令列與整合」頁最上面的設定精靈；已經裝好 kumi 的直接登入再啟動；
 * 沒辦法開瀏覽器時才用權杖。已經有電腦時只留要接另一台時用得到的。
 */
export function LocalAgentSetup({ hasMachines }: { readonly hasMachines: boolean }) {
  const origin = window.location.origin;
  if (hasMachines) {
    // 直接放進右欄（不包步驟清單的外框）：沒有序號，不用讓出序號的位置，左緣和上面的電腦清單對齊。
    return (
      <>
        <p className={accountStyles.note}>
          要接另一台電腦，照同樣的步驟：在那台電腦執行<WizardLink />，或執行 <code>{loginCommand(origin) ?? `${COMMAND_NAME} agent login`}</code> 再 <code>{ENABLE_COMMAND}</code>。沒辦法開瀏覽器時：
        </p>
        <PasteFallback origin={origin} />
      </>
    );
  }
  return (
    <ol className={styles.steps} aria-label="接上電腦的步驟">
      <li>
        <p>
          在那台電腦執行「命令列與整合」頁最上面的<WizardLink />（會裝好 <code>{COMMAND_NAME}</code>、登入，並問你要不要接上本機代理、要不要登入電腦時自動啟動），並且登入過 Claude Code 或 Codex。
        </p>
      </li>
      <li>
        <p>已經裝好 {COMMAND_NAME} 的，直接登入本機代理（瀏覽器會開頁面，按「允許」）：</p>
        <CopyBlock label="登入的指令" text={loginCommand(origin)} wrap />
      </li>
      <li>
        <p>再選一種啟動的方式：</p>
        <CopyBlock label="自動啟動的指令" text={ENABLE_COMMAND} />
        <p className={accountStyles.note}>登入電腦時自動在背景啟動（macOS、Windows），不必一直開著終端機。</p>
        <CopyBlock label="啟動代理的指令" text={START_COMMAND} />
        <p className={accountStyles.note}>只在這個視窗啟動，視窗要一直開著。</p>
      </li>
      <li>
        <p>沒辦法開瀏覽器時（例如透過 SSH）：建一把本機代理專用的權杖（只能領 AI 助理的工作、讀設定與項目、提出變更集，90 天後到期），再貼上登入。</p>
        <PasteFallback origin={origin} />
      </li>
      <li>
        <p>回到 AI 助理，在模型選單選「Claude Code（電腦名稱）」。這裡的清單會在電腦連上之後自己更新。</p>
      </li>
    </ol>
  );
}
