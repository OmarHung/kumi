import { Node } from '@tiptap/core';
import styles from './nodes.module.css';

/**
 * 不認得的區塊（原始的 HTML、註腳的定義、沒用到的連結定義）：唯讀的一塊原文，存回去一字不改。
 * 是 atom：游標進不去，整塊選取、刪掉。
 */
export const RawBlock = Node.create({
  name: 'rawBlock',
  group: 'block',
  atom: true,
  selectable: true,
  draggable: false,

  addAttributes() {
    return { source: { default: '' } };
  },

  parseHTML() {
    return [{ tag: 'div[data-raw-block]', priority: 100, getAttrs: (element) => ({ source: element.textContent }) }];
  },

  renderHTML({ node }) {
    return ['div', { 'data-raw-block': '', class: styles.rawBlock, contenteditable: 'false' }, String(node.attrs.source)];
  },

  renderText({ node }) {
    return String(node.attrs.source);
  },
});

/** 不認得的行內寫法（原始的 HTML、註腳的參照）：同樣是唯讀的原文。 */
export const RawInline = Node.create({
  name: 'rawInline',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  addAttributes() {
    return { source: { default: '' } };
  },

  parseHTML() {
    return [{ tag: 'span[data-raw-inline]', priority: 100, getAttrs: (element) => ({ source: element.textContent }) }];
  },

  renderHTML({ node }) {
    return ['span', { 'data-raw-inline': '', class: styles.rawInline, contenteditable: 'false' }, String(node.attrs.source)];
  },

  renderText({ node }) {
    return String(node.attrs.source);
  },
});
