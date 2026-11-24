import { useEffect, useState } from 'react';

/** 最新一次渲染的值，用 get() 讀。 */
export interface Latest<T> {
  get(): T;
}

class LatestBox<T> implements Latest<T> {
  constructor(private value: T) {}

  get(): T {
    return this.value;
  }

  set(value: T): void {
    this.value = value;
  }
}

/**
 * Tiptap 在建立編輯器時就抓住了事件處理函式，函式要讀到最新的 props，所以放在一個盒子裡，每次渲染之後更新。
 * 不用 ref：把「讀 ref 的函式」交給別人在渲染中呼叫，React 的規則不允許。
 */
export function useLatest<T>(value: T): Latest<T> {
  const [box] = useState(() => new LatestBox(value));
  useEffect(() => {
    box.set(value);
  });
  return box;
}
