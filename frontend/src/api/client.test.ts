import { beforeEach, describe, expect, it } from "vitest";
import { tokenStore } from "./client";

describe("tokenStore", () => {
  beforeEach(() => {
    localStorage.clear();
    tokenStore.clear();
  });

  it("returns null when nothing is stored", () => {
    expect(tokenStore.get()).toBeNull();
  });

  it("round-trips a token pair", () => {
    tokenStore.set({ accessToken: "a", refreshToken: "r" });

    expect(tokenStore.get()).toEqual({ accessToken: "a", refreshToken: "r" });
  });

  it("clears both tokens", () => {
    tokenStore.set({ accessToken: "a", refreshToken: "r" });
    tokenStore.clear();

    expect(tokenStore.get()).toBeNull();
  });

  it("survives corrupt storage rather than throwing", () => {
    localStorage.setItem("slingking.tokens", "{not json");

    expect(tokenStore.get()).toBeNull();
  });
});
