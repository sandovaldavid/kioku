export interface BridgeStatus {
  running: boolean;
  port: number;
  clients: number;
  protocolVersion: number;
  minProtocolVersion: number;
  maxProtocolVersion: number;
  pluginVersion: string;
  authenticationEnabled: boolean;
}

export function formatStatusBarText(running: boolean, port: number, clients: number): string {
  if (!running) {
    return "[offline] Kioku";
  }

  return clients > 0 ? `[online] Kioku :${port} (${clients})` : `[online] Kioku :${port}`;
}

export function statusBarCssClass(running: boolean): string {
  return running ? "kioku-status-online" : "kioku-status-offline";
}
