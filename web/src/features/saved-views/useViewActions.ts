import { useState } from 'react';
import { useSearchParams } from 'react-router';
import type { ProjectBlueprint } from '../../api/blueprint';
import { errorMessage } from '../../api/http';
import type { SavedView, SavedViewDraft } from '../../api/types';
import { savedViewParams, type ViewState } from '../../domain/savedViews';
import { keepPanelItem } from '../work-items/itemLinks';
import { projectViewApi, useViewMutations, type ViewApi } from './useSavedViewMutations';
import type { OpenDialog } from './ViewDialogs';

/**
 * 檢視選單的動作與狀態：套用、另存、重新命名、刪除、更新成目前的條件。
 * 「套用了哪一個」只記在這個畫面的狀態（重新整理就忘了）：網址上只有條件，
 * 兩個檢視條件一樣時分不出是哪個，記在網址上反而讓分享出去的連結帶著別人看不到的編號。
 */
export function useViewActions(projectKey: string, blueprint: ProjectBlueprint, current: ViewState) {
  return useViewActionsFor({
    api: projectViewApi(projectKey),
    current,
    paramsOf: (view) => savedViewParams(view, blueprint),
  });
}

interface ViewActionsSource {
  readonly api: ViewApi;
  /** 要存的版面與條件（「更新成目前的條件」「另存」用）。 */
  readonly current: SavedViewDraftBody;
  /** 套用一個檢視要導去的查詢字串（不含開著的面板）。 */
  readonly paramsOf: (view: SavedView) => URLSearchParams;
}

type SavedViewDraftBody = Pick<SavedViewDraft, 'layout' | 'query'>;

/** 套用是導去網址（push）：上一頁回到套用之前的條件。開著的面板留著（keepPanelItem）。 */
function useApplyView(paramsOf: ViewActionsSource['paramsOf']) {
  const [searchParams, setSearchParams] = useSearchParams();
  const [appliedId, setAppliedId] = useState<string | null>(null);
  function apply(view: SavedView) {
    setAppliedId(view.id);
    const next = keepPanelItem(paramsOf(view), searchParams);
    // 網址已經一樣就不導航：多一筆一模一樣的瀏覽紀錄，使用者按上一頁會以為沒反應。
    if (next.toString() !== searchParams.toString()) setSearchParams(next);
  }
  return { appliedId, setAppliedId, apply };
}

interface EditsContext {
  readonly mutations: ReturnType<typeof useViewMutations>;
  readonly current: SavedViewDraftBody;
  readonly setAppliedId: (update: (id: string | null) => string | null) => void;
  readonly setDialog: (dialog: OpenDialog | null) => void;
}

/** 另存、更新、重新命名、刪除：成功就關上對話框；更新失敗才開對話框說明，可以再試一次。 */
function useViewEdits({ mutations, current, setAppliedId, setDialog }: EditsContext) {
  const done = () => {
    setDialog(null);
  };
  // 不問名稱，選了就做；失敗才開對話框說明。
  function updateToCurrent(view: SavedView) {
    const draft = { name: view.name, layout: current.layout, query: current.query };
    mutations.replace.mutate(
      { id: view.id, draft },
      {
        onSuccess: done,
        onError: (error) => {
          setDialog({ kind: 'update', view, message: errorMessage(error) });
        },
      },
    );
  }

  function save(name: string) {
    const remember = (created: SavedView) => {
      setAppliedId(() => created.id);
      done();
    };
    mutations.create.mutate({ name, layout: current.layout, query: current.query }, { onSuccess: remember });
  }

  function rename(view: SavedView, name: string) {
    mutations.replace.mutate({ id: view.id, draft: { name, layout: view.layout, query: view.query } }, { onSuccess: done });
  }

  function remove(view: SavedView) {
    const forget = () => {
      setAppliedId((id) => (id === view.id ? null : id));
      done();
    };
    mutations.remove.mutate(view.id, { onSuccess: forget });
  }

  return { updateToCurrent, save, rename, remove };
}

/** 不綁專案的檢視動作：專案與「我的工作」各給自己的讀寫、網址與目前的條件。 */
export function useViewActionsFor({ api, current, paramsOf }: ViewActionsSource) {
  const mutations = useViewMutations(api);
  const { appliedId, setAppliedId, apply } = useApplyView(paramsOf);
  const [dialog, setDialog] = useState<OpenDialog | null>(null);
  const edits = useViewEdits({ mutations, current, setAppliedId, setDialog });

  function close() {
    mutations.create.reset();
    mutations.replace.reset();
    mutations.remove.reset();
    setDialog(null);
  }

  return { appliedId, dialog, setDialog, mutations, apply, close, ...edits };
}
