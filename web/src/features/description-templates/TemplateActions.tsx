import { Link } from 'react-router';
import type { DescriptionTemplate } from '../../api/types';
import { buttonClassName } from '../../components/Button/Button';
import { IconButton } from '../../components/IconButton/IconButton';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { editTemplatePath, newTemplatePath } from './templatePaths';
import styles from './DescriptionTemplates.module.css';

export interface TemplateActionHandlers {
  readonly onSetDefault: (template: DescriptionTemplate) => void;
  readonly onDelete: (template: DescriptionTemplate) => void;
  /** 有請求還在送：所有列的選單暫時打不開（連續點的話只有最後一次有回饋）。 */
  readonly isBusy: boolean;
}

function menuItems(template: DescriptionTemplate, handlers: TemplateActionHandlers): MenuItem[] {
  const setDefault: MenuItem = { key: 'set-default', label: '設成預設', icon: 'check', onSelect: () => { handlers.onSetDefault(template); } };
  // 刪除不能還原：放最後、危險色。
  const remove: MenuItem = { key: 'delete', label: '刪除', icon: 'delete', tone: 'danger', startsGroup: !template.isDefault, onSelect: () => { handlers.onDelete(template); } };
  return template.isDefault ? [remove] : [setDefault, remove];
}

/**
 * 一列最後的動作。組織的格式：「編輯」加「更多」（設成預設、刪除）；內建的改不動，只有「複製一份來改」。
 * 「更多」選好之後選單先關上、焦點回到按鈕，才開確認的對話框（和人員頁一樣）。
 */
export function TemplateActions({ template, ...handlers }: { readonly template: DescriptionTemplate } & TemplateActionHandlers) {
  if (template.isBuiltIn) {
    return (
      <Link to={newTemplatePath(template.id)} className={buttonClassName('secondary', 'sm')}>
        複製一份來改
      </Link>
    );
  }
  const label = `${template.name} 的動作`;
  return (
    <span className={styles.actions}>
      <Link to={editTemplatePath(template.id)} className={buttonClassName('secondary', 'sm')}>
        編輯
      </Link>
      <Menu
        menuLabel={label}
        align="end"
        items={menuItems(template, handlers)}
        isDisabled={handlers.isBusy}
        renderTrigger={(trigger) => <IconButton {...trigger} icon="more" label={label} size="sm" />}
      />
    </span>
  );
}
