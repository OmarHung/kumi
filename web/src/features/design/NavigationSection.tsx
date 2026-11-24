import { useState } from 'react';
import { useSearchParams } from 'react-router';
import { Button } from '../../components/Button/Button';
import { TextField } from '../../components/Field/Field';
import { IconButton } from '../../components/IconButton/IconButton';
import { NavTabs } from '../../components/NavTabs/NavTabs';
import { Breadcrumb, PageBar } from '../../components/PageBar/PageBar';
import { PageLayout } from '../../components/PageLayout/PageLayout';
import { SegmentedControl } from '../../components/SegmentedControl/SegmentedControl';
import { tableStyles } from '../../components/Table/Table';
import { Toolbar } from '../../components/Toolbar/Toolbar';
import { Demo, DemoSection } from './DemoSection';
import { SidePanelDemos } from './SidePanelDemo';
import styles from './Design.module.css';

// 展示用的頁列也會設分頁的標題（<title>）：用這一頁自己的標題，分頁的名字才不會被換掉。
const PAGE_TITLE = '設計元件';

function TabsDemo() {
  const [params] = useSearchParams();
  const isBoard = params.get('view') === 'board';
  return (
    <Demo name="NavTabs">
      <NavTabs
        label="檢視"
        tabs={[
          { key: 'table', label: '表格', to: '/design', isCurrent: !isBoard },
          { key: 'board', label: '看板', to: '/design?view=board', isCurrent: isBoard },
        ]}
      />
      <NavTabs
        label="設定的區塊"
        tabs={[
          {
            key: 'workflows',
            label: '流程',
            to: '/design',
            isCurrent: true,
            counts: [
              { value: 2, label: '2 項還沒送出的修改', tone: 'neutral' },
              { value: 1, label: '1 個要修正的問題', tone: 'danger' },
            ],
          },
          { key: 'types', label: '類型', to: '/design', isCurrent: false },
          { key: 'fields', label: '欄位', to: '/design', isCurrent: false },
          { key: 'roles', label: '角色', to: '/design', isCurrent: false },
        ]}
      />
    </Demo>
  );
}

function SegmentedControlDemo() {
  const [view, setView] = useState<'comments' | 'all'>('comments');
  return (
    <Demo name="SegmentedControl">
      <SegmentedControl
        label="顯示"
        options={[
          { value: 'comments', label: '留言' },
          { value: 'all', label: '全部' },
        ]}
        value={view}
        onChange={setView}
      />
    </Demo>
  );
}

function ItemPageBar() {
  return (
    <PageBar
      title={PAGE_TITLE}
      titleInBar={false}
      crumbs={[{ label: 'AGY', to: '/design', mono: true }, { label: 'AGY-3', to: '/design', mono: true }, { label: 'AGY-7', mono: true }]}
      actions={<IconButton icon="more" label="AGY-7 的更多動作（展示用）" />}
    />
  );
}

/** 工作區：頁列加工具列要在第 14 節的預算內（寬螢幕 48 ＋ 44），內容緊接工具列、自己捲。 */
function WorkspaceDemo() {
  const toolbar = (
    <Toolbar end={<IconButton icon="columns" label="欄位（展示用）" size="sm" variant="secondary" />}>
      <NavTabs
        label="檢視（展示用）"
        tabs={[
          { key: 'table', label: '表格', to: '/design', isCurrent: true },
          { key: 'board', label: '看板', to: '/design?view=board', isCurrent: false },
        ]}
      />
      <Button size="sm" variant="ghost">
        清除條件
      </Button>
    </Toolbar>
  );
  return (
    <Demo name="PageLayout（workspace）">
      <div className={styles.workspaceFrame} data-demo="workspace">
        <PageLayout variant="workspace" bar={<ItemPageBar />} toolbar={toolbar}>
          <div className={styles.workspaceScroll}>
            <table className={tableStyles.table}>
              <tbody>
                {['首頁設計稿', '官網需求', '驗收中的需求', '報價單'].map((title, index) => (
                  <tr key={title}>
                    <td className={tableStyles.mono}>AGY-{index + 1}</td>
                    <td>{title}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </PageLayout>
      </div>
    </Demo>
  );
}

function ListDemo() {
  const bar = (
    <div className={styles.fakeBar}>
      <Breadcrumb crumbs={[{ label: '專案', to: '/design' }, { label: 'AGY', to: '/design', mono: true }, { label: 'AGY-7', mono: true }]} />
    </div>
  );
  return (
    <Demo name="PageLayout（list）加 Toolbar">
      <div className={styles.frame}>
        <PageLayout
          variant="list"
          bar={bar}
          toolbar={
            <Toolbar end={<IconButton icon="filter" label="篩選（展示用）" size="sm" variant="secondary" />}>
              <TextField label="搜尋" className={styles.inlineField} />
            </Toolbar>
          }
        >
          <p className={styles.note}>頁列（這裡只放位置）、工具列、內容由上而下。這一頁本身是 reading 版面。</p>
        </PageLayout>
      </div>
    </Demo>
  );
}

export function NavigationSection() {
  return (
    <DemoSection title="導覽與版面">
      <TabsDemo />
      <SegmentedControlDemo />
      <Demo name="Breadcrumb">
        <Breadcrumb crumbs={[{ label: 'AGY', to: '/design', mono: true }, { label: '設定' }]} />
      </Demo>
      <Demo name="PageBar titleInBar={false}">
        <div className={styles.frame}>
          <ItemPageBar />
        </div>
      </Demo>
      <WorkspaceDemo />
      <SidePanelDemos />
      <ListDemo />
    </DemoSection>
  );
}
