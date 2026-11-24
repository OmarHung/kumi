import { Button } from '../../components/Button/Button';
import { CheckboxField, CheckboxGroup } from '../../components/CheckboxField/CheckboxField';
import { Icon, ICON_NAMES } from '../../components/Icon/Icon';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { Popover } from '../../components/Popover/Popover';
import { Demo, DemoSection } from './DemoSection';
import { DialogDemos } from './DialogDemos';
import styles from './Design.module.css';

const MORE_ITEMS: readonly MenuItem[] = [
  { key: 'type', label: '改類型', icon: 'edit', onSelect: () => undefined },
  { key: 'open', label: '在新分頁打開', icon: 'externalLink', onSelect: () => undefined },
  { key: 'archive', label: '封存', disabledReason: '這一版還沒有封存', onSelect: () => undefined },
  { key: 'delete', label: '刪除', icon: 'delete', tone: 'danger', startsGroup: true, onSelect: () => undefined },
];

const MOVE_ITEMS: readonly MenuItem[] = [
  { key: 'dev', label: '開發', onSelect: () => undefined },
  { key: 'qa', label: '內部驗收', onSelect: () => undefined },
  { key: 'client', label: '客戶驗收', disabledReason: '需要的角色：專案經理', startsGroup: true, onSelect: () => undefined },
];

const COLUMNS = ['類型', '負責人', '優先度', '到期日', '備註'];

function MenuDemos() {
  return (
    <>
      <Demo name="Menu">
        <Menu
          menuLabel="AGY-3 的更多動作"
          items={MORE_ITEMS}
          align="end"
          renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label="AGY-3 的更多動作" />}
        />
        <Menu
          menuLabel="把 AGY-3 移到"
          items={MOVE_ITEMS}
          renderTrigger={(trigger) => (
            <Button {...trigger} size="sm">
              移到…（沒有圖示）
            </Button>
          )}
        />
        <Menu
          menuLabel="送出中的選單"
          items={MORE_ITEMS}
          isDisabled
          renderTrigger={(trigger) => (
            <Button {...trigger} size="sm">
              送出中（打不開）
            </Button>
          )}
        />
      </Demo>
      <Demo name="Popover">
        <Popover label="顯示的欄位" renderTrigger={(trigger) => <Button {...trigger} size="sm">顯示的欄位</Button>}>
          <CheckboxGroup legend="顯示的欄位" isLegendHidden>
            {COLUMNS.map((column) => (
              <CheckboxField key={column} label={column} defaultChecked />
            ))}
          </CheckboxGroup>
        </Popover>
      </Demo>
    </>
  );
}

export function OverlaySection() {
  return (
    <DemoSection title="選單、浮層與對話框">
      <MenuDemos />
      <DialogDemos />
    </DemoSection>
  );
}

export function IconsSection() {
  return (
    <DemoSection title="圖示">
      <ul className={styles.icons}>
        {ICON_NAMES.map((name) => (
          <li key={name} className={styles.iconItem}>
            <Icon name={name} size={20} />
            <code>{name}</code>
          </li>
        ))}
      </ul>
    </DemoSection>
  );
}
