import { useRef, useState, type RefObject } from 'react';
import type { LocalEngineKind } from '../../api/aiTypes';
import type { HostAgent, HostSource } from '../../api/hostAgentTypes';
import { useSaveHostSource } from '../../api/hostAgents';
import { ApiError, isApiError } from '../../api/http';
import { hasErrors, mapApiErrorToFields, type FieldErrors, type FieldMapping } from '../../lib/formErrors';
import { engineLabel } from './hostAgentText';
import {
  draftForEngine,
  draftFromSource,
  emptySourceDraft,
  modelChoices,
  SOURCE_FIELDS,
  takenEngines,
  toSourceInput,
  validateSource,
  type SourceDraft,
  type SourceField,
} from './sourceDraft';

export interface SourceForm {
  readonly draft: SourceDraft;
  readonly errors: FieldErrors<SourceField>;
  /** 對不到欄位的伺服器錯誤（例如 ai.host_billing_not_shareable）。 */
  readonly formError: string | null;
  readonly isSaving: boolean;
  /** 每次送出後有欄位出錯就加一：畫面照它把焦點移到第一個有問題的欄位。 */
  readonly focusCount: number;
  readonly formRef: RefObject<HTMLFormElement | null>;
  /** 已有的來源（新增時，這台代理已經有來源的引擎不能再選）。 */
  readonly sources: readonly HostSource[];
  /** 能選的代理（新增時的選單）。 */
  readonly agents: readonly HostAgent[];
  readonly agent: HostAgent | undefined;
  readonly choices: ReturnType<typeof modelChoices>;
  readonly change: (patch: Partial<SourceDraft>, field?: SourceField) => void;
  /** 新增時選代理或引擎：名稱、模型、開關跟著引擎的狀態重新預設。 */
  readonly changeTarget: (agentId: string, engine: LocalEngineKind) => void;
  readonly toggleModel: (id: string) => void;
  readonly save: () => void;
}

interface FormArgs {
  /** null 是新增。 */
  readonly source: HostSource | null;
  readonly agents: readonly HostAgent[];
  readonly sources: readonly HostSource[];
  readonly onSaved: () => void;
}

/** 草稿與每個欄位的改法：改了欄位就清掉那個欄位上次的錯誤與表單上方的說明。 */
function useSourceDraft({ source, agents, sources }: Omit<FormArgs, 'onSaved'>) {
  const [draft, setDraft] = useState(() => (source === null ? emptySourceDraft(agents, sources) : draftFromSource(source)));
  const [errors, setErrors] = useState<FieldErrors<SourceField>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const clear = (...fields: readonly SourceField[]) => {
    setErrors((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !fields.some((field) => field === key))));
  };

  function change(patch: Partial<SourceDraft>, field?: SourceField) {
    setDraft((current) => ({ ...current, ...patch }));
    setFormError(null);
    if (field !== undefined) clear(field);
  }

  function changeTarget(agentId: string, engine: LocalEngineKind) {
    const nextAgent = agents.find((candidate) => candidate.id === agentId);
    // 換了代理：原本的引擎在新的代理上已經有來源時，換成還沒有的那個。
    const taken = takenEngines(agentId, sources);
    const free = taken.includes(engine) ? (['claude_code', 'codex'] as const).find((candidate) => !taken.includes(candidate)) : undefined;
    setDraft((current) => draftForEngine({ ...current, agentId, engine: free ?? engine }, nextAgent, `主機上的 ${engineLabel(current.engine)}`));
    setErrors({});
    setFormError(null);
  }

  function toggleModel(id: string) {
    setDraft((current) => {
      const isOn = current.enabledModels.includes(id);
      const enabledModels = isOn ? current.enabledModels.filter((model) => model !== id) : [...current.enabledModels, id];
      // 預設的模型被取消勾選：預設回到「不指定」，不留下指向沒開放的模型。
      const isDefaultRemoved = isOn && current.defaultModel === id;
      return { ...current, enabledModels, defaultModel: isDefaultRemoved ? '' : current.defaultModel, defaultEffort: isDefaultRemoved ? '' : current.defaultEffort };
    });
    clear('enabledModels', 'defaultModel');
  }

  return { draft, errors, setErrors, formError, setFormError, change, changeTarget, toggleModel };
}

const LIMITS_PREFIX = 'limits.';

/** 後端的上限欄位路徑是 limits.xxx，畫面上的欄位叫 xxx。 */
function withFlatPaths(failure: unknown): unknown {
  if (!isApiError(failure)) return failure;
  const details = failure.details.map((detail) => (detail.path?.startsWith(LIMITS_PREFIX) === true ? { ...detail, path: detail.path.slice(LIMITS_PREFIX.length) } : detail));
  return new ApiError({ status: failure.status, code: failure.code, message: failure.message, details, data: failure.data });
}

/** 畫面上有地方顯示的欄位：編輯時代理與引擎是唯讀文字，它們的錯誤併進表單上方的說明，不會憑空不見。 */
function mappingFor(isNew: boolean): FieldMapping<SourceField> {
  return { fields: isNew ? SOURCE_FIELDS : SOURCE_FIELDS.filter((field) => field !== 'agentId' && field !== 'engine') };
}

/** 新增或編輯組織來源的表單：草稿、前端檢查（和後端同一套規則）、伺服器的細項分到欄位。 */
export function useSourceForm({ source, agents, sources, onSaved }: FormArgs): SourceForm {
  const { draft, errors, setErrors, formError, setFormError, change, changeTarget, toggleModel } = useSourceDraft({ source, agents, sources });
  const [focusCount, setFocusCount] = useState(0);
  const formRef = useRef<HTMLFormElement>(null);
  const save = useSaveHostSource();
  const agent = agents.find((candidate) => candidate.id === draft.agentId);

  function submit() {
    if (save.isPending) return;
    const found = validateSource(draft);
    setErrors(found);
    if (hasErrors(found)) {
      setFocusCount((count) => count + 1);
      return;
    }
    setFormError(null);
    save.mutate(
      { id: source?.id ?? null, input: toSourceInput(draft) },
      {
        onSuccess: onSaved,
        onError: (failure) => {
          const mapped = mapApiErrorToFields(withFlatPaths(failure), mappingFor(source === null));
          setErrors(mapped.fieldErrors);
          setFormError(mapped.formError);
          if (hasErrors(mapped.fieldErrors)) setFocusCount((count) => count + 1);
        },
      },
    );
  }

  return { draft, errors, formError, isSaving: save.isPending, focusCount, formRef, sources, agents, agent, choices: modelChoices(agent, draft.engine, source), change, changeTarget, toggleModel, save: submit };
}
