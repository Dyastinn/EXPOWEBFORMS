/**
 * Centralized API client for the Expo app.
 *
 * All API calls go through `apiFetch` — it attaches:
 *   X-Api-Key: mobile-app-v1   (application identifier, always)
 *   Authorization: Bearer …    (when a token is stored)
 *
 * Token storage:
 *   Native (iOS/Android): expo-secure-store, keyed by TOKEN_SECURE_KEY.
 *     Android limit is ~2048 bytes per entry; keep JWT claims minimal to stay clear.
 *   Web: in-memory variable. SecureStore is unavailable on web, and the web POC
 *     receives its token via postMessage from the parent shell — no persistent
 *     storage is required for that flow. This has XSS exposure; a production
 *     web build would need a different strategy (httpOnly cookie or BFF pattern).
 *
 * Refresh tokens are out of scope for this bare-minimum POC. Add single-flight
 * refresh here (around the `apiFetch` function) when refresh tokens are introduced.
 */

import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';

export const API_BASE    = 'http://localhost:5050';
export const API_KEY     = 'mobile-app-v1';
const TOKEN_SECURE_KEY   = 'access_token';

// In-memory fallback for web (SecureStore is unavailable there).
let _webToken: string | null = null;

export async function getToken(): Promise<string | null> {
  if (Platform.OS === 'web') return _webToken;
  return SecureStore.getItemAsync(TOKEN_SECURE_KEY);
}

export async function setToken(token: string): Promise<void> {
  if (Platform.OS === 'web') {
    _webToken = token;
    return;
  }
  // Android SecureStore has a ~2048-byte per-value limit; a JWT with minimal
  // claims (sub, name, jti, roles) is well within range.
  await SecureStore.setItemAsync(TOKEN_SECURE_KEY, token);
}

export async function clearToken(): Promise<void> {
  if (Platform.OS === 'web') {
    _webToken = null;
    return;
  }
  await SecureStore.deleteItemAsync(TOKEN_SECURE_KEY);
}

type FetchInit = Parameters<typeof fetch>[1];

/**
 * Fetch wrapper that automatically attaches X-Api-Key and Authorization headers.
 * `path` is relative to API_BASE (e.g. "/api/customers?page=1").
 */
export async function apiFetch(path: string, init?: FetchInit): Promise<Response> {
  const token   = await getToken();
  const headers: Record<string, string> = {
    'X-Api-Key': API_KEY,
    ...(init?.headers as Record<string, string> | undefined ?? {}),
  };
  if (token) headers['Authorization'] = `Bearer ${token}`;

  return fetch(`${API_BASE}${path}`, { ...init, headers });
}
