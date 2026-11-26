import { useEffect, useRef, useState, type RefObject } from 'react';
import type { HostAgentPairing } from '../../api/hostAgentTypes';
import { useHostOverview } from '../../api/hostAgents';
import { useCurrentUser } from '../../auth/CurrentUser';
import { Button } from '../../components/Button/Button';
import { SettingsSection } from '../../components/SettingsSection/SettingsSection';
import { Skeleton } from '../../components/Skeleton/Skeleton';
import { ErrorState, RefetchErrorNotice } from '../../components/StatusViews/StatusViews';
import { NoPermission } from '../../components/StatusViews/NoPermission';
import { AiSetupMap } from '../ai/AiSetupMap';
import { AgentNameDialog } from './AgentDialogs';
import { AgentList } from './AgentList';
import { CodeDialog } from './CodeDialog';
import { HOST_AGENTS_NOTE, PAIR_COMMAND, SOURCES_NOTE } from './hostAgentText';
import { SourceList } from './SourceList';
import styles from './HostAgents.module.css';

const NO_PERMISSION = '只有組織的擁有者可以設定主機 AI 代理。';
const SKELETON_ROWS = 2;

interface OverviewBodyProps {
  readonly overview: ReturnType<typeof useHostOverview>;
  readonly onPaired: (pairing: HostAgentPairing) => void;
  readonly onAdd: () => void;
  readonly addRef: RefObject<HTMLButtonElement | null>;
}

function OverviewBody({ overview, onPaired, onAdd, addRef }: OverviewBodyProps) {
  const data = overview.data;
  if (overview.isPending) return <Skeleton label="正在載入主機 AI 代理…" rows={SKELETON_ROWS} />;
  if (data === undefined) {
    return (
      <ErrorState
        error={overview.error}
        title="主機 AI 代理載入失敗"
        onRetry={() => {
          void overview.refetch();
        }}
      />
    );
  }
  return (
    <>
      <SettingsSection title="主機" description={<p>{HOST_AGENTS_NOTE}</p>}>
        <div className={styles.group}>
          <AgentList agents={data.agents} now={overview.dataUpdatedAt} onPaired={onPaired} />
          <div>
            <Button ref={addRef} onClick={onAdd}>
              新增主機代理
            </Button>
          </div>
        </div>
      </SettingsSection>
      <SettingsSection title="來源" description={<p>{SOURCES_NOTE}</p>}>
        <div className={styles.group}>
          <SourceList agents={data.agents} sources={data.sources} />
        </div>
      </SettingsSection>
      {/* 背景重讀偶爾失敗的說明放在最下面：出現時不推動上面的清單。 */}
      <RefetchErrorNotice query={overview} message="無法更新主機 AI 代理，畫面上可能是舊的。" />
    </>
  );
}

function PairingCodeDialog({ pairing, onClose }: { readonly pairing: HostAgentPairing; readonly onClose: () => void }) {
  return (
    <CodeDialog
      title="配對主機代理"
      description={`「${pairing.agent.name}」`}
      code={pairing.pairingCode}
      codeLabel="配對碼"
      expiresAt={pairing.pairingExpiresAt}
      command={PAIR_COMMAND}
      commandLabel="配對的指令"
      onClose={onClose}
    />
  );
}

/**
 * 「主機 AI 代理」的內容（internal/HANDOFF-host-agent.md 第 9 節）：只有組織擁有者掛上這個元件
 * （呼叫端看 role；真正的檢查在後端，管理員打 API 也是 403）。代理與來源每 5 秒重讀一次。
 * 配對碼只放在這裡的 state、顯示在對話框裡，不進查詢的快取。
 */
function HostAgentsContent() {
  const overview = useHostOverview();
  const [isAdding, setIsAdding] = useState(false);
  const [pairing, setPairing] = useState<HostAgentPairing | null>(null);
  const addRef = useRef<HTMLButtonElement>(null);
  // 新增代理：名稱對話框關掉、配對碼對話框再關掉之後，焦點回到「新增主機代理」（對話框打開時它已經不在焦點上，自己不會回去）。
  const wasAddingPairing = useRef(false);
  useEffect(() => {
    if (pairing === null && wasAddingPairing.current) {
      wasAddingPairing.current = false;
      addRef.current?.focus();
    }
  }, [pairing]);
  return (
    <>
      {/* 對照放在讀取狀態之外：載入中、讀不到時照樣在最上面，資料到了不換位置。 */}
      <AiSetupMap page="host" />
      <OverviewBody
        overview={overview}
        onPaired={setPairing}
        onAdd={() => {
          setIsAdding(true);
        }}
        addRef={addRef}
      />
      {isAdding && (
        <AgentNameDialog
          agent={null}
          onPaired={(created) => {
            wasAddingPairing.current = true;
            setPairing(created);
          }}
          onClose={() => {
            setIsAdding(false);
          }}
        />
      )}
      {pairing !== null && <PairingCodeDialog pairing={pairing} onClose={() => { setPairing(null); }} />}
    </>
  );
}

/**
 * 設定中心的「主機 AI 代理」：只有組織擁有者（管理員的次導覽沒有這一項，
 * 打了網址也只說沒有權限、不去讀；後端一律 403）。
 */
export function HostAgentsPage() {
  const user = useCurrentUser();
  return user.role === 'owner' ? <HostAgentsContent /> : <NoPermission message={NO_PERMISSION} />;
}
