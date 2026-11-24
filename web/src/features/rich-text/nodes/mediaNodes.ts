import { Node } from '@tiptap/core';
import { ReactNodeViewRenderer } from '@tiptap/react';
import { attachmentIdOf } from '../../../components/Markdown/markdownUrls';
import { AttachmentFileView, ImageView, UploadPlaceholderView } from './MediaViews';

const inlineAtom = {
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,
  draggable: false,
} as const;

/** 從網頁貼上的圖片只收附件（編輯器自己複製出去的）與 http(s)：data 網址會吃掉長度上限，file、blob 在別人的瀏覽器裡沒有意義。 */
function isPastableImageSource(src: string): boolean {
  return attachmentIdOf(src) !== null || /^https?:\/\//i.test(src);
}

/** 圖片（行內、atom）：src 是 attachment:<GUID> 時顯示圖片，其他網址不載入。 */
export const ImageNode = Node.create({
  name: 'image',
  ...inlineAtom,

  addAttributes() {
    return { src: { default: '' }, alt: { default: '' }, title: { default: null } };
  },

  parseHTML() {
    return [{ tag: 'img[src]', getAttrs: (element) => (isPastableImageSource(element.getAttribute('src') ?? '') ? null : false) }];
  },

  renderHTML({ HTMLAttributes }) {
    return ['img', HTMLAttributes];
  },

  renderText({ node }) {
    return `![${String(node.attrs.alt)}](${String(node.attrs.src)})`;
  },

  addNodeView() {
    return ReactNodeViewRenderer(ImageView, { as: 'span' });
  },
});

/** 附件的檔案連結 [名稱](attachment:編號)：編輯器裡是圖示加檔名的小卡。 */
export const AttachmentFileNode = Node.create({
  name: 'attachmentFile',
  ...inlineAtom,

  addAttributes() {
    return {
      attachmentId: { default: '', parseHTML: (element: HTMLElement) => element.getAttribute('data-attachment-file') },
      name: { default: '', parseHTML: (element: HTMLElement) => element.textContent },
    };
  },

  parseHTML() {
    return [{ tag: 'span[data-attachment-file]', priority: 100 }];
  },

  renderHTML({ node }) {
    return ['span', { 'data-attachment-file': String(node.attrs.attachmentId) }, String(node.attrs.name)];
  },

  renderText({ node }) {
    return `[${String(node.attrs.name)}](attachment:${String(node.attrs.attachmentId)})`;
  },

  addNodeView() {
    return ReactNodeViewRenderer(AttachmentFileView, { as: 'span' });
  },
});

/** 上傳中的記號：只在編輯器裡產生，寫成 Markdown 就是記號的原文（上傳完成時換成參照）。 */
export const UploadPlaceholderNode = Node.create({
  name: 'uploadPlaceholder',
  ...inlineAtom,

  addAttributes() {
    return {
      placeholder: { default: '', parseHTML: (element: HTMLElement) => element.getAttribute('data-upload-placeholder') },
      name: { default: '', parseHTML: (element: HTMLElement) => element.textContent },
      isImage: { default: false, parseHTML: (element: HTMLElement) => element.getAttribute('data-is-image') === 'true' },
    };
  },

  parseHTML() {
    return [{ tag: 'span[data-upload-placeholder]', priority: 100 }];
  },

  renderHTML({ node }) {
    return ['span', { 'data-upload-placeholder': String(node.attrs.placeholder), 'data-is-image': String(node.attrs.isImage) }, String(node.attrs.name)];
  },

  renderText({ node }) {
    return String(node.attrs.placeholder);
  },

  addNodeView() {
    return ReactNodeViewRenderer(UploadPlaceholderView, { as: 'span' });
  },
});
