import type {
  BridgeCapability,
  BridgeErrorCode,
  BridgeMessage,
  KiokuSettings,
  RuntimeCommand,
} from "./types";

export type CommandRisk =
  | "read-only"
  | "ui-navigation"
  | "editor-mutation"
  | "third-party-integration"
  | "vault-wide"
  | "unsafe-custom";

export interface CommandAuthorization {
  allowed: boolean;
  risk: CommandRisk;
  code?: BridgeErrorCode;
  message?: string;
}

const EDITOR_MUTATION_COMMANDS: ReadonlySet<RuntimeCommand> = new Set([
  "reload-snippets",
  "insert-at-cursor",
  "replace-selection",
  "create-note-ui",
]);

const THIRD_PARTY_COMMANDS: ReadonlySet<RuntimeCommand> = new Set([
  "run-dataview-query",
  "run-templater",
  "evaluate-templater-in-file",
  "run-linter",
]);

const SAFE_OBSIDIAN_COMMANDS = new Map<string, CommandRisk>([
  ["app:toggle-left-sidebar", "ui-navigation"],
  ["app:toggle-right-sidebar", "ui-navigation"],
  ["markdown:toggle-preview", "ui-navigation"],
  ["editor:fold-all", "ui-navigation"],
  ["editor:unfold-all", "ui-navigation"],
]);

function enabled(value: boolean | undefined, fallback: boolean): boolean {
  return value ?? fallback;
}

export function getBridgeCapabilities(settings: KiokuSettings): BridgeCapability[] {
  const capabilities: BridgeCapability[] = ["read", "ui-navigation"];

  if (enabled(settings.allowEditorMutations, true)) {
    capabilities.push("editor-mutation");
  }
  if (enabled(settings.allowThirdPartyIntegrations, false)) {
    capabilities.push("third-party-dataview", "third-party-templater", "third-party-linter");
  }
  if (enabled(settings.allowVaultWideOperations, false)) {
    capabilities.push("vault-wide");
  }
  if (
    enabled(settings.allowUnsafeCommands, false) &&
    (settings.additionalAllowedCommandIds?.length ?? 0) > 0
  ) {
    capabilities.push("unsafe-command");
  }

  return capabilities;
}

export function authorizeCommand(
  message: Extract<BridgeMessage, { command: RuntimeCommand }>,
  settings: KiokuSettings
): CommandAuthorization {
  const { command } = message;

  if (command === "trigger-command") {
    const commandId = (message.payload as { commandId: string }).commandId;
    const safeRisk = SAFE_OBSIDIAN_COMMANDS.get(commandId);
    if (safeRisk) {
      return { allowed: true, risk: safeRisk };
    }

    const explicitAllowlist = settings.additionalAllowedCommandIds ?? [];
    if (enabled(settings.allowUnsafeCommands, false) && explicitAllowlist.includes(commandId)) {
      return { allowed: true, risk: "unsafe-custom" };
    }

    return {
      allowed: false,
      risk: "unsafe-custom",
      code: "COMMAND_DENIED",
      message:
        "The requested Obsidian command is not in Kioku's safe allowlist. Enable unsafe command mode and explicitly list the command ID to allow it.",
    };
  }

  if (command === "run-linter-vault") {
    if (!enabled(settings.allowThirdPartyIntegrations, false)) {
      return {
        allowed: false,
        risk: "third-party-integration",
        code: "CAPABILITY_DENIED",
        message: "Third-party plugin integrations are disabled.",
      };
    }
    if (!enabled(settings.allowVaultWideOperations, false)) {
      return {
        allowed: false,
        risk: "vault-wide",
        code: "CAPABILITY_DENIED",
        message: "Vault-wide bridge operations are disabled.",
      };
    }
    return { allowed: true, risk: "vault-wide" };
  }

  if (THIRD_PARTY_COMMANDS.has(command)) {
    return enabled(settings.allowThirdPartyIntegrations, false)
      ? { allowed: true, risk: "third-party-integration" }
      : {
          allowed: false,
          risk: "third-party-integration",
          code: "CAPABILITY_DENIED",
          message: "Third-party plugin integrations are disabled.",
        };
  }

  if (EDITOR_MUTATION_COMMANDS.has(command)) {
    return enabled(settings.allowEditorMutations, true)
      ? { allowed: true, risk: "editor-mutation" }
      : {
          allowed: false,
          risk: "editor-mutation",
          code: "CAPABILITY_DENIED",
          message: "Editor mutation commands are disabled.",
        };
  }

  return {
    allowed: true,
    risk:
      command.startsWith("get-") || command === "is-obsidian-ready" ? "read-only" : "ui-navigation",
  };
}
