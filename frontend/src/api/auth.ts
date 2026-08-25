import { api, tokenStore } from "./client";

export interface User {
  id: number;
  email: string;
  displayName: string;
}

interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  user: User;
}

export async function login(email: string, password: string): Promise<User> {
  const { data } = await api.post<LoginResponse>("/api/v1/auth/login", {
    email,
    password,
  });

  tokenStore.set({
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
  });

  return data.user;
}

export async function logout(): Promise<void> {
  const tokens = tokenStore.get();
  if (tokens) {
    try {
      await api.post("/api/v1/auth/logout", {
        refreshToken: tokens.refreshToken,
      });
    } catch {
      // A failed logout call still logs the user out locally.
    }
  }
  tokenStore.clear();
}

export async function getMe(): Promise<User> {
  const { data } = await api.get<User>("/api/v1/auth/me");
  return data;
}
