import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { createPage, deletePage, movePage } from '../../../api/knowledge';
import type { KnowledgePage, KnowledgePageDeletion, KnowledgePageNode, KnowledgePageTree } from '../../../api/knowledgeTypes';
import { queryKeys } from '../../../api/queryKeys';
import { cellErrorMessage } from '../../item-table/cells/EditableCell';
import { knowledgePageUrl, knowledgeSpaceUrl } from '../knowledgeLinks';
import type { Trashed } from '../page/TrashedArrival';
import type { NewPageState } from '../tree/PageTree';
import { applyMove, type MovePlan } from '../tree/treeDrop';
import { ancestorsOf, descendantNumbers, type TreeModel } from '../tree/treeModel';

/** 新增、刪除之後帶到下一個畫面的狀態：新建的頁面直接進編輯，刪除的提示旁邊有「復原」。 */
export interface PageEntryState {
  readonly edit?: boolean;
  readonly notice?: string;
  readonly trashed?: { readonly spaceKey: string; readonly pageNumber: number; readonly title: string };
}

const NEW_PAGE_FIELDS = ['title', 'parentNumber', 'beforeNumber', 'body'] as const;

function withoutSubtree(tree: KnowledgePageTree, model: TreeModel, node: KnowledgePageNode): KnowledgePageTree {
  const gone = descendantNumbers(model, node);
  return { ...tree, pages: tree.pages.filter((page) => page.number !== node.number && !gone.has(page.number)) };
}

/** 在樹上新增：建立之後進到那一頁的編輯。onCreated：換頁之後（窄螢幕用來收起頁面面板）。 */
function useCreatePage(spaceKey: string, onCreated: () => void) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [target, setTarget] = useState<{ readonly parentNumber: number | null } | null>(null);
  const create = useMutation({
    mutationFn: (input: { readonly title: string; readonly parentNumber: number | null }) =>
      createPage(spaceKey, { parentNumber: input.parentNumber, title: input.title, beforeNumber: null }),
    // 等樹重抓好再換頁：新頁面要已經在樹上，麵包屑與標題才不會先閃一下空間的名稱。
    onSuccess: async (page: KnowledgePage) => {
      queryClient.setQueryData(queryKeys.knowledgePage(spaceKey, page.number), page);
      await queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeTree(spaceKey) });
      setTarget(null);
      const state: PageEntryState = { edit: true };
      void navigate(knowledgePageUrl(spaceKey, page.number), { state });
      onCreated();
    },
  });
  const newPage: NewPageState | null =
    target === null
      ? null
      : { parentNumber: target.parentNumber, isPending: create.isPending, error: create.isError ? cellErrorMessage(create.error, NEW_PAGE_FIELDS) : null };
  return {
    newPage,
    // 送出中忽略：reset 只會讓畫面以為沒在送，請求照跑、成功後照樣換頁，輸入框卻又能再送一次。
    startNewPage: (parentNumber: number | null) => {
      if (create.isPending) return;
      create.reset();
      setTarget({ parentNumber });
    },
    cancelNewPage: () => {
      if (create.isPending) return;
      create.reset();
      setTarget(null);
    },
    submitNewPage: (title: string) => {
      if (target !== null) create.mutate({ title, parentNumber: target.parentNumber });
    },
  };
}

/** 搬移（拖曳與對話框共用）：先改畫面，失敗再換回來，成功換成伺服器回的樹。 */
function useMovePage(spaceKey: string) {
  const queryClient = useQueryClient();
  const treeKey = queryKeys.knowledgeTree(spaceKey);
  const spaceQueryKey = queryKeys.knowledgeSpace(spaceKey);
  return useMutation({
    mutationFn: (input: { readonly pageNumber: number; readonly plan: MovePlan }) => movePage(spaceKey, input.pageNumber, input.plan),
    onMutate: async ({ pageNumber, plan }) => {
      await queryClient.cancelQueries({ queryKey: treeKey });
      const previous = queryClient.getQueryData<KnowledgePageTree>(treeKey);
      if (previous !== undefined) queryClient.setQueryData(treeKey, { ...previous, pages: applyMove(previous.pages, pageNumber, plan) });
      return { previous };
    },
    // 失敗也重抓：只換回舊的快照，別人同時改過的樹就一直是舊的。
    onError: (_error, _input, context) => {
      if (context?.previous !== undefined) queryClient.setQueryData(treeKey, context.previous);
      void queryClient.invalidateQueries({ queryKey: spaceQueryKey });
    },
    onSuccess: (tree) => {
      queryClient.setQueryData(treeKey, tree);
      // 被搬的頁面與它底下所有頁面的麵包屑（ancestors）、繼承來的限制都是頁面自己的資料：整個空間一起重抓，不等過期。
      void queryClient.invalidateQueries({ queryKey: spaceQueryKey });
    },
  });
}

/** 正在看的頁面被刪了（它自己或它的上層）：回到被刪的那一頁的上層，最上層就回空間首頁；沒被影響是 null（留在原地）。 */
function destinationAfterDelete(spaceKey: string, model: TreeModel, node: KnowledgePageNode, currentNumber: number | null): string | null {
  const current = currentNumber === null ? undefined : model.nodeOf(currentNumber);
  const isViewing = current !== undefined && (current.number === node.number || ancestorsOf(model, current).some((ancestor) => ancestor.number === node.number));
  if (!isViewing) return null;
  const parent = model.parentOf(node);
  return parent === null ? knowledgeSpaceUrl(spaceKey) : knowledgePageUrl(spaceKey, parent.number);
}

/** 被刪的頁面（連同底下的）在快取裡的資料：留著的話，按上一頁回到它的網址會先看到舊內容、還能編輯。 */
function forgetDeletedPages(queryClient: QueryClient, spaceKey: string, model: TreeModel, node: KnowledgePageNode) {
  [node.number, ...descendantNumbers(model, node)].forEach((number) => {
    queryClient.removeQueries({ queryKey: queryKeys.knowledgePage(spaceKey, number) });
  });
}

/**
 * 刪除：連同底下的頁面放進垃圾桶。刪的是正在看的頁面才換頁（帶「已放進垃圾桶」與「復原」）；
 * 刪別頁時留在原地：換頁會把焦點搬走、打斷正在編輯的頁面，所以提示（本身就是即時區域）與「復原」就地顯示。
 */
function useDeletePage(spaceKey: string, model: TreeModel, currentNumber: number | null, onTrashedInPlace: (trashed: Trashed) => void) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const treeKey = queryKeys.knowledgeTree(spaceKey);
  return useMutation({
    mutationFn: (node: KnowledgePageNode) => deletePage(spaceKey, node.number),
    onSuccess: (_deletion: KnowledgePageDeletion, node) => {
      const tree = queryClient.getQueryData<KnowledgePageTree>(treeKey);
      if (tree !== undefined) queryClient.setQueryData(treeKey, withoutSubtree(tree, model, node));
      void queryClient.invalidateQueries({ queryKey: queryKeys.knowledgeSpace(spaceKey) });
      const notice = `已把「${node.title}」放進垃圾桶。`;
      const destination = destinationAfterDelete(spaceKey, model, node, currentNumber);
      if (destination === null) {
        forgetDeletedPages(queryClient, spaceKey, model, node);
        onTrashedInPlace({ spaceKey, pageNumber: node.number, title: node.title });
        return;
      }
      const state: PageEntryState = { notice, trashed: { spaceKey, pageNumber: node.number, title: node.title } };
      // 換完頁再清：正在看的那一頁還掛著時清掉，會先閃一下「找不到」。
      void Promise.resolve(navigate(destination, { state })).then(() => {
        forgetDeletedPages(queryClient, spaceKey, model, node);
      });
    },
  });
}

export interface SpaceActionOptions {
  /** 新頁面建好、換頁之後（窄螢幕用來收起頁面面板）。 */
  readonly onCreated: () => void;
  /** 刪的不是正在看的頁面、所以沒有換頁：提示與「復原」就地顯示。 */
  readonly onTrashedInPlace: (trashed: Trashed) => void;
}

/**
 * 空間裡改動頁面樹的三件事：在樹上新增、搬移、刪除。對話框與面板由 SpaceShell 管，這裡只有資料與換頁。
 */
export function useSpaceActions(spaceKey: string, model: TreeModel, currentNumber: number | null, options: SpaceActionOptions) {
  return {
    ...useCreatePage(spaceKey, options.onCreated),
    move: useMovePage(spaceKey),
    remove: useDeletePage(spaceKey, model, currentNumber, options.onTrashedInPlace),
  };
}

export type SpaceActions = ReturnType<typeof useSpaceActions>;
