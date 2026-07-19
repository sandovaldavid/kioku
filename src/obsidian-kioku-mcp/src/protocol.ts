import {
  BRIDGE_CAPABILITIES,
  PROTOCOL_VERSION,
  type AuthPayload,
  type BridgeCapability,
  type BridgeCommand,
  type BridgeErrorCode,
  type BridgeMessage,
  type BridgeResponse,
  type CommandPayloadMap,
} from "./types";

export const MAX_MESSAGE_BYTES = 256 * 1024;
export const MAX_REQUEST_ID_LENGTH = 128;
export const MAX_PATH_LENGTH = 1024;
export const MAX_TEXT_LENGTH = 128 * 1024;
export const MAX_QUERY_LENGTH = 16 * 1024;
export const MAX_COMMAND_ID_LENGTH = 256;

const COMMANDS: ReadonlySet<string> = new Set<BridgeCommand>([
  "auth",
  "open-file",
  "get-active-note",
  "get-vault-path",
  "is-obsidian-ready",
  "get-app-version",
  "get-open-notes",
  "trigger-command",
  "toggle-reading-mode",
  "get-selection",
  "fold-all-headings",
  "unfold-all-headings",
  "reload-snippets",
  "insert-at-cursor",
  "replace-selection",
  "create-note-ui",
  "scroll-to-block",
  "open-in-split",
  "run-dataview-query",
  "run-templater",
  "evaluate-templater-in-file",
  "run-linter",
  "run-linter-vault",
  "get-installed-plugins",
]);

const NO_PAYLOAD_COMMANDS: ReadonlySet<BridgeCommand> = new Set([
  "get-active-note",
  "get-vault-path",
  "is-obsidian-ready",
  "get-app-version",
  "get-open-notes",
  "toggle-reading-mode",
  "get-selection",
  "fold-all-headings",
  "unfold-all-headings",
  "reload-snippets",
  "run-linter-vault",
  "get-installed-plugins",
]);

export interface ProtocolValidationError {
  code: "INVALID_MESSAGE" | "INVALID_PAYLOAD";
  message: string;
  requestId?: string;
}

export type ProtocolValidationResult =
  | { ok: true; message: BridgeMessage }
  | { ok: false; error: ProtocolValidationError };

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isNonEmptyString(value: unknown, maxLength: number): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= maxLength;
}

function isOptionalString(value: unknown, maxLength: number): value is string | undefined {
  return value === undefined || (typeof value === "string" && value.length <= maxLength);
}

function isIntegerInRange(value: unknown, minimum: number, maximum: number): value is number {
  return Number.isInteger(value) && Number(value) >= minimum && Number(value) <= maximum;
}

export function isSafeVaultPath(value: unknown): value is string {
  if (!isNonEmptyString(value, MAX_PATH_LENGTH) || value.includes("\0")) {
    return false;
  }

  const normalized = value.replaceAll("\\", "/");
  if (
    normalized.startsWith("/") ||
    normalized.startsWith("//") ||
    /^[a-zA-Z]:\//.test(normalized)
  ) {
    return false;
  }

  const segments = normalized.split("/");
  return segments.every((segment) => segment !== ".." && segment !== ".");
}

function isCapability(value: unknown): value is BridgeCapability {
  return typeof value === "string" && (BRIDGE_CAPABILITIES as readonly string[]).includes(value);
}

function validateAuthPayload(payload: unknown): payload is AuthPayload {
  if (!isRecord(payload)) return false;
  if (!isIntegerInRange(payload.minProtocolVersion, 1, 1000)) return false;
  if (!isIntegerInRange(payload.maxProtocolVersion, 1, 1000)) return false;
  if (payload.minProtocolVersion > payload.maxProtocolVersion) return false;
  if (!isOptionalString(payload.token, 4096)) return false;
  if (!isOptionalString(payload.clientName, 128)) return false;
  if (!isOptionalString(payload.clientVersion, 128)) return false;

  if (payload.requestedCapabilities !== undefined) {
    if (
      !Array.isArray(payload.requestedCapabilities) ||
      payload.requestedCapabilities.length > 32
    ) {
      return false;
    }
    if (!payload.requestedCapabilities.every(isCapability)) return false;
  }

  return true;
}

function validatePayload<Command extends BridgeCommand>(
  command: Command,
  payload: unknown
): payload is CommandPayloadMap[Command] {
  if (NO_PAYLOAD_COMMANDS.has(command)) {
    return (
      payload === undefined ||
      payload === null ||
      (isRecord(payload) && Object.keys(payload).length === 0)
    );
  }

  if (command === "auth") return validateAuthPayload(payload);
  if (!isRecord(payload)) return false;

  switch (command) {
    case "open-file":
    case "create-note-ui":
    case "open-in-split":
      return isSafeVaultPath(payload.path);
    case "trigger-command":
      return isNonEmptyString(payload.commandId, MAX_COMMAND_ID_LENGTH);
    case "insert-at-cursor":
    case "replace-selection":
      return isNonEmptyString(payload.text, MAX_TEXT_LENGTH);
    case "scroll-to-block":
      return isNonEmptyString(payload.blockId, 128) && /^[a-zA-Z0-9_-]+$/.test(payload.blockId);
    case "run-dataview-query":
      return isNonEmptyString(payload.query, MAX_QUERY_LENGTH);
    case "run-templater":
      return (
        isSafeVaultPath(payload.templatePath) &&
        (payload.targetNote === undefined || isSafeVaultPath(payload.targetNote))
      );
    case "evaluate-templater-in-file":
      return isSafeVaultPath(payload.notePath);
    case "run-linter":
      return payload.notePath === undefined || isSafeVaultPath(payload.notePath);
    default:
      return false;
  }
}

export function parseBridgeMessage(value: unknown): ProtocolValidationResult {
  if (!isRecord(value)) {
    return {
      ok: false,
      error: { code: "INVALID_MESSAGE", message: "Bridge message must be an object." },
    };
  }

  const requestId = isNonEmptyString(value.requestId, MAX_REQUEST_ID_LENGTH)
    ? value.requestId
    : undefined;

  if (!requestId) {
    return {
      ok: false,
      error: { code: "INVALID_MESSAGE", message: "A valid requestId is required." },
    };
  }

  if (typeof value.command !== "string" || !COMMANDS.has(value.command)) {
    return {
      ok: false,
      error: { code: "INVALID_MESSAGE", message: "Unknown bridge command.", requestId },
    };
  }

  if (!isIntegerInRange(value.protocolVersion, 1, 1000)) {
    return {
      ok: false,
      error: {
        code: "INVALID_MESSAGE",
        message: "A valid protocolVersion is required.",
        requestId,
      },
    };
  }

  const command = value.command as BridgeCommand;
  if (!validatePayload(command, value.payload)) {
    return {
      ok: false,
      error: { code: "INVALID_PAYLOAD", message: "The command payload is invalid.", requestId },
    };
  }

  return { ok: true, message: value as BridgeMessage };
}

export function bridgeError(
  code: BridgeErrorCode,
  message: string,
  requestId?: string,
  protocolVersion: number = PROTOCOL_VERSION
): BridgeResponse {
  return {
    requestId,
    success: false,
    errorCode: code,
    error: `[error] [${code}] ${message}`,
    protocolVersion,
  };
}
