import { vi } from 'vitest';
import { signedInAs } from '../../test/api';
import { exampleOwner, smkDetail } from '../../test/fixtures';
import { serveCycles, serveMilestones } from '../../test/planning';
import type { Cycle, Milestone } from '../../api/planning';
import type { ProjectDetail } from '../../api/types';
import { renderApp } from '../../test/renderApp';

/** 「今天」是 2026-11-18；只換掉 Date，計時器照常。呼叫端在 afterEach 還原（vi.useRealTimers）。 */
export function fakeToday(): void {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 10, 18, 9));
}

interface PlanningSetup {
  readonly cycles?: readonly Cycle[];
  readonly milestones?: readonly Milestone[];
  readonly detail?: ProjectDetail;
}

/** 以專案管理員（預設）打開 SMK 的規劃頁，週期與里程碑用有狀態的假端點。 */
export function openPlanning({ cycles = [], milestones = [], detail = smkDetail() }: PlanningSetup = {}) {
  signedInAs(exampleOwner, { details: [detail] });
  const cycleServer = serveCycles('SMK', cycles);
  const milestoneServer = serveMilestones('SMK', milestones);
  const app = renderApp('/projects/SMK/planning');
  return { ...app, cycleServer, milestoneServer };
}
