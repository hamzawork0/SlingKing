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

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5210",
  headers: { "Content-Type": "application/json" },
});

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
  } catch {
    tokenStore.clear();
    return null;
  }
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const request = error.config as InternalAxiosRequestConfig & {
      _retried?: boolean;
    };

    const isAuthCall = request?.url?.includes("/auth/");

    if (error.response?.status !== 401 || request?._retried || isAuthCall) {
      return Promise.reject(error);
    }

    request._retried = true;

    refreshInFlight ??= refreshTokens().finally(() => {
      refreshInFlight = null;
    });

    const tokens = await refreshInFlight;

    if (!tokens) {
      window.location.assign("/login");
      return Promise.reject(error);
    }

    request.headers.Authorization = `Bearer ${tokens.accessToken}`;
    return api.request(request);
  },
);
