import type { AiEffort, AiOptions, AiSelection } from '../../api/aiTypes';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import { Menu, type MenuItem } from '../../components/Menu/Menu';
import { effortLabel } from '../ai/aiLabels';
import { defaultLabel, groupName } from './ModelPicker';
import { findModel, withModel } from './selection';
import { sendKeyDescription, setSendKey, SEND_KEYS, type SendKey } from './sendKey';
import styles from './Assistant.module.css';

interface ComposerSettingsProps {
  readonly options: AiOptions;
  readonly selection: AiSelection;
  readonly onChange: (selection: AiSelection) => void;
  readonly sendKey: SendKey;
}

function modelItems(options: AiOptions, selection: AiSelection, onChange: (selection: AiSelection) => void): readonly MenuItem[] {
  const hasGroups = options.connections.length > 1;
  return options.connections.flatMap((connection) =>
    connection.models.map((model, index): MenuItem => ({
      key: `model:${connection.id}:${model.id}`,
      label: model.displayName,
      isChecked: connection.id === selection.connectionId && model.id === selection.model,
      ...(connection.notice === null ? {} : { disabledReason: connection.notice }),
      ...(index === 0 ? { groupLabel: hasGroups ? `模型：${groupName(connection)}` : '模型' } : {}),
      onSelect: () => {
        onChange(withModel(options, selection, connection.id, model.id));
      },
    })),
  );
}

function effortItems(efforts: readonly AiEffort[], defaultEffort: AiEffort | null, selection: AiSelection, onChange: (selection: AiSelection) => void): readonly MenuItem[] {
  if (efforts.length === 0) return [];
  const choices: readonly (AiEffort | null)[] = [null, ...efforts];
  return choices.map((effort, index): MenuItem => ({
    key: `effort:${effort ?? 'default'}`,
    label: effort === null ? defaultLabel(defaultEffort) : effortLabel(effort),
    isChecked: selection.effort === effort,
    ...(index === 0 ? { groupLabel: '思考深度' } : {}),
    onSelect: () => {
      onChange({ ...selection, effort });
    },
  }));
}

function sendKeyItems(sendKey: SendKey): readonly MenuItem[] {
  return SEND_KEYS.map((key, index): MenuItem => ({
    key: `send:${key}`,
    label: sendKeyDescription(key),
    isChecked: key === sendKey,
    ...(index === 0 ? { groupLabel: '送出的按法' } : {}),
    onSelect: () => {
      setSendKey(key);
    },
  }));
}

/**
 * 窄螢幕（48rem 以下）輸入框的設定鈕（2026-11-27 使用者看過手機版設計稿後採用）：模型、思考深度與送出的按法收成一顆，
 * 鈕上只有圖示加目前模型的名稱，其餘一列放不下的三個下拉不再各佔一格。用現有的 Menu（不另做底部抽屜）：三組選一個的項目，
 * 選了就關上、焦點回到鈕。寬螢幕仍是 ModelPicker 與 SendKeyPicker。
 */
export function ComposerSettings({ options, selection, onChange, sendKey }: ComposerSettingsProps) {
  const model = findModel(options, selection);
  const effortText = selection.effort === null ? defaultLabel(model?.defaultEffort ?? null) : effortLabel(selection.effort);
  const modelName = model?.displayName ?? selection.model;
  const items = [
    ...modelItems(options, selection, onChange),
    ...effortItems(model?.efforts ?? [], model?.defaultEffort ?? null, selection, onChange),
    ...sendKeyItems(sendKey),
  ];
  const label = `模型與思考深度：${modelName}，${effortText}`;
  return (
    <Menu
      menuLabel="模型、思考深度與送出的按法"
      align="end"
      items={items}
      renderTrigger={(trigger) => (
        <Button {...trigger} size="sm" variant="ghost" aria-label={label} title={label} className={styles.settingsButton}>
          <Icon name="settings" />
          <span className={styles.settingsName}>{modelName}</span>
        </Button>
      )}
    />
  );
}
