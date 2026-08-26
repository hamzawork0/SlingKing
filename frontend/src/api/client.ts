import axios, {
  type AxiosError,
  type InternalAxiosRequestConfig,
} from "axios";

const STORAGE_KEY = "slingking.tokens";

export interface TokenPair {
  accessToken: string;
  refreshToken: string;
}

export const tokenStore = {
  get(): TokenPair | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw) as TokenPair;
      return parsed.accessToken && parsed.refreshToken ? parsed : null;
    } catch {
      // Corrupt or unavailable storage is a logged-out user, not a crash.
      return null;
    }
  },
  set(tokens: TokenPair) {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(tokens));
    } catch {
      /* private mode — the session simply will not persist */
    }
  },
  clear() {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      /* nothing to do */
    }
  },
};

// No fallback. A default here would let a missing VITE_API_BASE_URL ship a build that
// silently talks to a dead port — every request failing in a way that looks like the
// API being down rather than the app being misconfigured. Fail at load instead.
const baseURL: string = import.meta.env.VITE_API_BASE_URL;
if (!baseURL) {
  throw new Error(
    "VITE_API_BASE_URL is not set. Copy frontend/.env.example to frontend/.env.",
  );
}

export const api = axios.create({
  baseURL,
  headers: { "Content-Type": "application/json" },
});

/**
 * True when the request never got an answer — server down, DNS gone, cable out.
 * A rejected credential is a 401 with a body; an unreachable server is neither,
 * and treating the two alike is how a brief outage logs someone out for good.
 */
export function isNetworkError(error: unknown): boolean {
  return axios.isAxiosError(error) && !error.response;
}

api.interceptors.request.use((config) => {
  const tokens = tokenStore.get();
  if (tokens) {
    config.headers.Authorization = `Bearer ${tokens.accessToken}`;
  }
  return config;
});

// One refresh at a time. Without this, five parallel 401s trigger five rotations
// and four of them lose the race — logging the user out mid-session.
let refreshInFlight: Promise<TokenPair | null> | null = null;

async function refreshTokens(): Promise<TokenPair | null> {
  const current = tokenStore.get();
  if (!current) return null;

  try {
    const { data } = await axios.post<{
      accessToken: string;
      refreshToken: string;
    }>(`${api.defaults.baseURL}/api/v1/auth/refresh`, {
      refreshToken: current.refreshToken,
    });

    const next = {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
    };
    tokenStore.set(next);
    return next;
  } catch (error) {
    // Keep the tokens when the server was simply unreachable — they may still be good.
    // Only a real rejection means this refresh token is dead.
    if (!isNetworkError(error)) tokenStore.clear();
    return null;
  }
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const request = error.config as InternalAxiosRequestConfig & {
      _retried?: boolean;
    };

    // Only the three endpoints that would recurse: refreshing a login is meaningless,
    // and retrying the refresh call through the refresh path is an infinite loop.
    // /auth/me is a normal authenticated call and MUST be refreshable — it is the one
    // request the app makes on every page load, so excluding it disabled refresh entirely.
    const isAuthCall = /\/auth\/(login|refresh|logout)$/.test(request?.url ?? "");

    if (error.response?.status !== 401 || request?._retried || isAuthCall) {
      return Promise.reject(error);
    }

    request._retried = true;

    refreshInFlight ??= refreshTokens().finally(() => {
      refreshInFlight = null;
    });

    const tokens = await refreshInFlight;

    if (!tokens) {
      // Bounce to the login screen only if the refresh actually failed. If the tokens
      // survived, the server was unreachable, not the session dead — let the caller
      // surface that rather than throwing the user out over a dropped connection.
      if (!tokenStore.get()) window.location.assign("/login");
      return Promise.reject(error);
    }

    request.headers.Authorization = `Bearer ${tokens.accessToken}`;
    return api.request(request);
  },
);
