import { describe, it, expect } from "vitest";
import { formatStatusBarText, statusBarCssClass } from "./status";

describe("formatStatusBarText", () => {
  it("returns the offline label when the bridge is not running", () => {
    expect(formatStatusBarText(false, 7765, 0)).toBe("[offline] Kioku");
  });

  it("returns the offline label regardless of a stale client count", () => {
    expect(formatStatusBarText(false, 7765, 3)).toBe("[offline] Kioku");
  });

  it("returns the port with no client count when running with zero clients", () => {
    expect(formatStatusBarText(true, 7765, 0)).toBe("[online] Kioku :7765");
  });

  it("returns the port and client count when running with clients connected", () => {
    expect(formatStatusBarText(true, 7765, 1)).toBe("[online] Kioku :7765 (1)");
    expect(formatStatusBarText(true, 7765, 3)).toBe("[online] Kioku :7765 (3)");
  });
});

describe("statusBarCssClass", () => {
  it("returns the online class when running", () => {
    expect(statusBarCssClass(true)).toBe("kioku-status-online");
  });

  it("returns the offline class when not running", () => {
    expect(statusBarCssClass(false)).toBe("kioku-status-offline");
  });
});
