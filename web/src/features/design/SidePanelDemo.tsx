import { useState } from 'react';
import { Button } from '../../components/Button/Button';
import { TextField } from '../../components/Field/Field';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { SidePanel } from '../../components/SidePanel/SidePanel';
import { SidePanelLayout, type SidePanelLayoutKind } from '../../components/SidePanel/SidePanelLayout';
import { tableStyles } from '../../components/Table/Table';
import { ariaKeyShortcuts, formatShortcut, SHORTCUTS, useShortcut } from '../../lib/shortcuts';
import { updateThenFocus } from '../../lib/updateThenFocus';
import { Demo } from './DemoSection';
import styles from './Design.module.css';

interface DemoItem {
  readonly key: string;
  readonly title: string;
  readonly state: string;
}

const ITEMS: readonly DemoItem[] = [
  { key: 'AGY-11', title: '登入頁改版', state: '進行中' },
  { key: 'AGY-12', title: '付款流程', state: '未開始' },
  { key: 'AGY-13', title: '客服信箱', state: '內部驗收' },
  { key: 'AGY-14', title: '季報', state: '完成' },
  { key: 'AGY-15', title: '上線前檢查', state: '待規劃' },
];

const PANEL_ACTIONS: readonly MenuItem[] = [
  { key: 'type', label: '改類型', icon: 'edit', onSelect: () => undefined },
  { key: 'delete', label: '刪除', icon: 'delete', tone: 'danger', startsGroup: true, onSelect: () => undefined },
];

const LONG_TEXT =
  '面板自己直向捲動，標題列黏在上方。寬螢幕（64rem 以上）是右邊一欄，清單跟著縮窄；以下蓋在清單上面、從右邊滑進來。' +
  '清單照常可以操作：點另一列就換成那一個項目，焦點留在清單上。';

function rowId(layout: SidePanelLayoutKind, key: string): string {
  return `design-panel-${layout}-${key}`;
}

/** 開著哪一個、上一個／下一個（Alt+↑／↓）、關上之後焦點回到清單上那一筆。 */
function usePanelDemo(layout: SidePanelLayoutKind) {
  const [openKey, setOpenKey] = useState<string | null>(null);
  const index = ITEMS.findIndex((item) => item.key === openKey);
  const hasPrev = index > 0;
  const hasNext = index >= 0 && index < ITEMS.length - 1;
  const step = (delta: number) => {
    const next = ITEMS[index + delta];
    if (next !== undefined) setOpenKey(next.key);
  };
  const close = () => {
    if (openKey === null) return;
    updateThenFocus(() => {
      setOpenKey(null);
    }, [rowId(layout, openKey)]);
  };
  useShortcut(SHORTCUTS.itemPrev, () => {
    step(-1);
  }, { isEnabled: hasPrev });
  useShortcut(SHORTCUTS.itemNext, () => {
    step(1);
  }, { isEnabled: hasNext });
  return { open: ITEMS[index], setOpenKey, close, step, hasPrev, hasNext };
}

type PanelDemoState = ReturnType<typeof usePanelDemo>;

function PanelHeaderActions({ demo }: { readonly demo: PanelDemoState }) {
  return (
    <>
      <IconButton
        icon="arrowUp"
        label="上一個項目"
        aria-keyshortcuts={ariaKeyShortcuts(SHORTCUTS.itemPrev)}
        aria-disabled={!demo.hasPrev || undefined}
        onClick={() => {
          demo.step(-1);
        }}
      />
      <IconButton
        icon="arrowDown"
        label="下一個項目"
        aria-keyshortcuts={ariaKeyShortcuts(SHORTCUTS.itemNext)}
        aria-disabled={!demo.hasNext || undefined}
        onClick={() => {
          demo.step(1);
        }}
      />
      <IconButton icon="expand" label="展開成整頁（展示用）" />
    </>
  );
}

function PanelBody({ item }: { readonly item: DemoItem }) {
  const keys = `${formatShortcut(SHORTCUTS.itemPrev)}／${formatShortcut(SHORTCUTS.itemNext)}`;
  return (
    <div className={styles.panelBody}>
      <p className={styles.note}>
        {keys} 換上一個、下一個；{formatShortcut(SHORTCUTS.closePanel)} 關閉（輸入框裡的 Esc 不算，開著的選單先關）。
      </p>
      <dl className={styles.panelFields}>
        <dt>狀態</dt>
        <dd>{item.state}</dd>
        <dt>標題</dt>
        <dd>{item.title}</dd>
      </dl>
      <TextField key={item.key} label="留言（展示用）" defaultValue="" />
      <Menu
        menuLabel={`${item.key} 的動作（展示用）`}
        items={PANEL_ACTIONS}
        renderTrigger={(trigger) => (
          <Button {...trigger} size="sm">
            更多動作
          </Button>
        )}
      />
      {[1, 2, 3].map((part) => (
        <p key={part} className={styles.lead}>
          {LONG_TEXT}
        </p>
      ))}
    </div>
  );
}

function DemoRows({ layout, demo }: { readonly layout: SidePanelLayoutKind; readonly demo: PanelDemoState }) {
  return (
    <table aria-label={`展示的項目（${layout}）`} className={tableStyles.table}>
      <tbody>
        {ITEMS.map((item) => (
          <tr key={item.key} aria-current={demo.open?.key === item.key || undefined} className={styles.panelRow}>
            <td className={tableStyles.mono}>{item.key}</td>
            <td>
              <button
                type="button"
                id={rowId(layout, item.key)}
                className={styles.rowButton}
                onClick={() => {
                  demo.setOpenKey(item.key);
                }}
              >
                {item.title}
              </button>
            </td>
            <td>{item.state}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function PanelDemo({ layout }: { readonly layout: SidePanelLayoutKind }) {
  const demo = usePanelDemo(layout);
  const panel = demo.open !== undefined && (
    <SidePanel
      label={`項目 ${demo.open.key}`}
      title={<span className={tableStyles.mono}>{demo.open.key}</span>}
      headerActions={<PanelHeaderActions demo={demo} />}
      onClose={demo.close}
    >
      <PanelBody item={demo.open} />
    </SidePanel>
  );
  const rows = <DemoRows layout={layout} demo={demo} />;
  return (
    <SidePanelLayout layout={layout} panel={panel}>
      {layout === 'fill' ? <div className={styles.workspaceScroll}>{rows}</div> : rows}
    </SidePanelLayout>
  );
}

/**
 * SidePanel 的展示：點一列開面板。fill 放在固定高度的框裡（工作區是 100dvh，這裡裁著看）；
 * page 跟著這一頁捲，面板黏在頁列下面（64rem 以下蓋在畫面右邊）。
 */
export function SidePanelDemos() {
  return (
    <>
      <Demo name="SidePanel（layout fill）">
        <div className={styles.panelFrame} data-demo="side-panel">
          <PanelDemo layout="fill" />
        </div>
      </Demo>
      <Demo name="SidePanel（layout page）">
        <div className={styles.panelPage}>
          <PanelDemo layout="page" />
        </div>
      </Demo>
    </>
  );
}
