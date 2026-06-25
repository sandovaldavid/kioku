export class Logger {
  private readonly name: string;

  constructor(name: string) {
    this.name = name;
  }

  debug(message: string, ...args: unknown[]): void {
    console.debug(`[${this.name}] [debug] ${message}`, ...args);
  }

  info(message: string, ...args: unknown[]): void {
    console.log(`[${this.name}] [info] ${message}`, ...args);
  }

  warn(message: string, ...args: unknown[]): void {
    console.warn(`[${this.name}] [warn] ${message}`, ...args);
  }

  error(message: string, ...args: unknown[]): void {
    console.error(`[${this.name}] [error] ${message}`, ...args);
  }
}

export const log = new Logger("Kioku");
