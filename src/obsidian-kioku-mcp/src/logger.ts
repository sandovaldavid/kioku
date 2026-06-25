export enum LogLevel {
  Debug = 0,
  Info = 1,
  Warn = 2,
  Error = 3,
  None = 4,
}

export class Logger {
  private readonly name: string;
  private readonly minLevel: LogLevel;

  constructor(name: string, minLevel: LogLevel = LogLevel.Debug) {
    this.name = name;
    this.minLevel = minLevel;
  }

  debug(message: string, ...args: unknown[]): void {
    if (this.minLevel <= LogLevel.Debug) {
      console.debug(`[${this.name}] [debug] ${message}`, ...args);
    }
  }

  info(message: string, ...args: unknown[]): void {
    if (this.minLevel <= LogLevel.Info) {
      console.log(`[${this.name}] [info] ${message}`, ...args);
    }
  }

  warn(message: string, ...args: unknown[]): void {
    if (this.minLevel <= LogLevel.Warn) {
      console.warn(`[${this.name}] [warn] ${message}`, ...args);
    }
  }

  error(message: string, ...args: unknown[]): void {
    if (this.minLevel <= LogLevel.Error) {
      console.error(`[${this.name}] [error] ${message}`, ...args);
    }
  }
}

export const log = new Logger("Kioku");
