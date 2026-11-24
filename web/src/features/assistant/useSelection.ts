import { useMemo, useState } from 'react';
import type { AiOptions, AiSelection } from '../../api/aiTypes';
import { readStoredSelection, resolveSelection, storeSelection } from './selection';

/** 記在瀏覽器的選擇；現在不能選了（連線被刪、模型不再開放）時退回組織的預設。整頁與浮動的面板共用同一份記錄。 */
export function useSelection(options: AiOptions): readonly [AiSelection | null, (next: AiSelection) => void] {
  const [chosen, setChosen] = useState(readStoredSelection);
  const selection = useMemo(() => resolveSelection(options, chosen), [options, chosen]);
  const choose = (next: AiSelection) => {
    setChosen(next);
    storeSelection(next);
  };
  return [selection, choose];
}
