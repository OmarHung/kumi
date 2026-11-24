import type { AiConnectionOption, AiEffort, AiOptions, AiSelection } from '../../api/aiTypes';
import { Select, type SelectOption } from '../../components/Select/Select';
import { DEFAULT_EFFORT_LABEL, effortLabel } from '../ai/aiLabels';
import { findModel, withModel } from './selection';

const SEPARATOR = '|';
const PERSONAL_SUFFIX = '（我的）';
const DEFAULT_EFFORT_VALUE = '';

interface ModelPickerProps {
  readonly options: AiOptions;
  readonly selection: AiSelection;
  readonly onChange: (selection: AiSelection) => void;
}

/** 自己的連線在組名後面加「（我的）」：和組織的同名時分得出來，也提醒費用算在自己的帳號。 */
function groupName(connection: AiConnectionOption): string {
  return connection.personal ? `${connection.name}${PERSONAL_SUFFIX}` : connection.name;
}

/** 依連線分組（組名是連線名稱）；只有一個連線時不分組。選項的值是「連線編號|模型代號」：連線編號是固定長度的編號，不含 |。 */
function modelOptions(options: AiOptions): readonly SelectOption[] {
  const hasGroups = options.connections.length > 1;
  return options.connections.flatMap((connection) =>
    connection.models.map((model, index): SelectOption => ({
      value: `${connection.id}${SEPARATOR}${model.id}`,
      label: model.displayName,
      // 主機來源現在不能用：整組的模型都不能選，原因（伺服器給的一句話）寫在選項的第二行。
      ...(connection.notice === null ? {} : { disabledReason: connection.notice }),
      ...(hasGroups && index === 0 ? { groupLabel: groupName(connection) } : {}),
    })),
  );
}

/** 「預設」：知道實際用哪一級時寫「預設（中）」。 */
function defaultLabel(defaultEffort: AiEffort | null): string {
  return defaultEffort === null ? DEFAULT_EFFORT_LABEL : `${DEFAULT_EFFORT_LABEL}（${effortLabel(defaultEffort)}）`;
}

function effortOptions(efforts: readonly AiEffort[], defaultEffort: AiEffort | null): readonly SelectOption[] {
  return [
    { value: DEFAULT_EFFORT_VALUE, label: defaultLabel(defaultEffort) },
    ...efforts.map((effort) => ({ value: effort, label: effortLabel(effort) })),
  ];
}

/** 輸入框下面的「模型」與「思考深度」：自己畫的下拉。模型不能選思考深度時，第二個不顯示。 */
export function ModelPicker({ options, selection, onChange }: ModelPickerProps) {
  const model = findModel(options, selection);
  const efforts = model?.efforts ?? [];
  return (
    <>
      <Select
        aria-label="模型"
        size="sm"
        width="content"
        value={`${selection.connectionId}${SEPARATOR}${selection.model}`}
        options={modelOptions(options)}
        onChange={(value) => {
          const at = value.indexOf(SEPARATOR);
          onChange(withModel(options, selection, value.slice(0, at), value.slice(at + 1)));
        }}
      />
      {efforts.length > 0 && (
        <Select
          aria-label="思考深度"
          size="sm"
          width="content"
          value={selection.effort ?? DEFAULT_EFFORT_VALUE}
          options={effortOptions(efforts, model?.defaultEffort ?? null)}
          onChange={(value) => {
            onChange({
              ...selection,
              effort: efforts.find((effort) => effort === value) ?? null,
            });
          }}
        />
      )}
    </>
  );
}
