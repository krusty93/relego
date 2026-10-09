import { describe, expect, it } from "vitest";
import { PROFILE_VERSION } from "../src/profile";

describe("extension scaffold", () => {
  it("exposes a parse-profile version", () => {
    expect(typeof PROFILE_VERSION).toBe("string");
    expect(PROFILE_VERSION.length).toBeGreaterThan(0);
  });
});
