import { setupServer } from 'msw/node';

/** 所有測試共用的假後端。每個測試用 server.use 加上自己需要的端點，結束後會被清掉。 */
export const server = setupServer();
