// =============================================================================
// DTO Types（对齐后端返回；只定义“我们会用到的字段”）
// =============================================================================

export type TradingSystemStatus = {
  dataCollector: string;
  sentimentAnalyst: string;
  technicalAnalyst: string;
  coordinator: string;
  riskManager: string;
  executor: string;
  tradeAudit: string;
  aiWarsUploader: string;
};

export type AgentsResponse = {
  agents: Array<{ name: string; status: string }>;
};

export type TickerResponse = {
  symbol: string;
  lastPrice: number;
  bidPrice: number;
  askPrice: number;
  volume24h: number;
  change24h: number;
  high24h: number;
  low24h: number;
  timestamp: string;
};

export type BalanceInfo = {
  currency: string;
  balance: number;
  available: number;
  frozen: number;
};

export type OrderRequest = {
  symbol: string;
  side: "buy" | "sell";
  orderType: "limit" | "market";
  force?: "normal" | "postOnly" | "fok" | "ioc";
  quantity: string;
  price?: string;
  clientOrderId?: string;
};

export type OrderResult = {
  success: boolean;
  orderId?: string | null;
  clientOrderId?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
};

export type CancelOrderResult = {
  success: boolean;
  orderId?: string | null;
  clientOrderId?: string | null;
  errorCode?: string | null;
  errorMessage?: string | null;
};

export type OrderInfo = {
  orderId: string;
  clientOrderId?: string | null;
  symbol: string;
  side: string;
  orderType: string;
  status: string;
  price: number;
  quantity: number;
  filledQuantity: number;
  filledPrice: number;
  fee: number;
  createTime: string;
  updateTime?: string | null;
};


