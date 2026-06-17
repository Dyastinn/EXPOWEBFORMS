import { StatusBar } from 'expo-status-bar';
import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { API_BASE, apiFetch, setToken } from './lib/apiClient';

// ── Config ────────────────────────────────────────────────────────────────────
const PAGE_SIZE = 5;

// The trusted shell origin for the iframe postMessage flow.
// This is a security check — not used to fetch tokens.
const SHELL_ORIGIN = ['http://localhost:5000','http://localhost:5001'];

// ── Types ─────────────────────────────────────────────────────────────────────
type Customer = {
  customerId: number;
  name:       string;
  email:      string;
  totalSpend: number;
  tier:       'Gold' | 'Silver' | 'Bronze';
};
type PagedResult = {
  items:      Customer[];
  page:       number;
  pageSize:   number;
  totalCount: number;
  totalPages: number;
};
type FormState   = { name: string; email: string; totalSpend: string };
// 'iframe'  — token delivered via postMessage from the parent shell
// 'login'   — user authenticated directly with their own credentials
type TokenSource = 'iframe' | 'login';
type LogEntry    = { ts: string; msg: string; detail?: string };
type JwtParts    = { header: Record<string, unknown>; payload: Record<string, unknown> };

// ── Helpers ───────────────────────────────────────────────────────────────────
function nowTs() { return new Date().toISOString().slice(11, 23); }

function decodeJwtParts(token: string): JwtParts | null {
  try {
    const b64 = (s: string) =>
      JSON.parse(atob(s.replace(/-/g, '+').replace(/_/g, '/'))) as Record<string, unknown>;
    const [h, p] = token.split('.');
    return { header: b64(h), payload: b64(p) };
  } catch { return null; }
}

function fmtUnix(v: unknown): string {
  if (typeof v !== 'number') return String(v);
  return new Date(v * 1000).toLocaleTimeString();
}

function expCountdown(v: unknown): string {
  if (typeof v !== 'number') return '';
  const secs = Math.round((v * 1000 - Date.now()) / 1000);
  if (secs < 0) return ' — EXPIRED';
  return ` — ${Math.floor(secs / 60)}m ${secs % 60}s remaining`;
}

// ── Constants ─────────────────────────────────────────────────────────────────
const TIER_COLORS: Record<string, string> = {
  Gold: '#B8860B', Silver: '#708090', Bronze: '#8B4513',
};
const EMPTY_FORM: FormState = { name: '', email: '', totalSpend: '' };
const badgeColor: Record<TokenSource, { backgroundColor: string }> = {
  iframe: { backgroundColor: '#4a3fa0' },
  login:  { backgroundColor: '#1a6b3c' },
};

// ── Component ─────────────────────────────────────────────────────────────────
export default function App() {
  // Auth mode is set once on mount and never changes:
  //   'iframe'      — we are embedded; wait for postMessage from the parent shell
  //   'credentials' — standalone / native; user must log in themselves
  const [authMode, setAuthMode] = useState<'iframe' | 'credentials' | null>(null);

  const [customers,   setCustomers]   = useState<Customer[]>([]);
  const [page,        setPage]        = useState(1);
  const [totalPages,  setTotalPages]  = useState(1);
  const [status,      setStatus]      = useState('');
  const [loading,     setLoading]     = useState(false);
  const [tokenSource, setTokenSource] = useState<TokenSource | null>(null);

  // Login form (credentials mode only)
  const [loginUser,    setLoginUser]    = useState('');
  const [loginPass,    setLoginPass]    = useState('');
  const [loginError,   setLoginError]   = useState('');
  const [loginLoading, setLoginLoading] = useState(false);

  // Customer create/edit modal
  const [showForm,        setShowForm]        = useState(false);
  const [editingCustomer, setEditingCustomer] = useState<Customer | null>(null);
  const [form,            setForm]            = useState<FormState>(EMPTY_FORM);
  const [formError,       setFormError]       = useState('');
  const [saving,          setSaving]          = useState(false);

  // Debug panel
  const [showDebug, setShowDebug] = useState(false);
  const [log,       setLog]       = useState<LogEntry[]>([]);
  const [jwtParts,  setJwtParts]  = useState<JwtParts | null>(null);
  const [rawToken,  setRawToken]  = useState<string | null>(null);

  const addLog = useCallback((msg: string, detail?: string) => {
    setLog(prev => [{ ts: nowTs(), msg, detail }, ...prev].slice(0, 100));
  }, []);

  const acceptToken = useCallback(async (token: string, via: string) => {
    await setToken(token);
    setRawToken(token);
    setJwtParts(decodeJwtParts(token));
    addLog(`Token accepted [via ${via}]`, `${token.slice(0, 36)}…`);
  }, [addLog]);

  // ── Auth setup (runs once) ─────────────────────────────────────────────────
  useEffect(() => {
    if (Platform.OS === 'web' && window.parent !== window) {
      // Iframe: token arrives via postMessage from the parent shell.
      // We do not fetch it ourselves — the shell is the auth authority here.
      setAuthMode('iframe');
      addLog('Detected iframe context', `waiting for postMessage from iframe`);

      const onMessage = async (event: MessageEvent) => {

        addLog(
          'postMessage received',
          `origin=${event.origin}  type=${(event.data as { type?: string })?.type ?? '?'}`,
        );

        if (event.source !== window.parent) {
          addLog('Ignored: source is not window.parent');
          return;
        }
        // Verify the message came from the known shell origin (security check).
        if (!SHELL_ORIGIN.includes(event.origin)) {
          addLog('Rejected: untrusted origin', event.origin);
          return;
        }
        const msg = event.data as { type?: string; token?: string };
        if (msg?.type !== 'AUTH_TOKEN' || !msg.token) {
          addLog('Ignored: wrong type or missing token', JSON.stringify(msg));
          return;
        }

        window.removeEventListener('message', onMessage);
        addLog('Token validated — origin + source checks passed');
        await acceptToken(msg.token, 'postMessage');
        setTokenSource('iframe');
        loadPage(1);
      };

      window.addEventListener('message', onMessage);
      return () => window.removeEventListener('message', onMessage);
    }

    // Standalone browser tab or native: user logs in with their own credentials.
    setAuthMode('credentials');
    addLog(
      Platform.OS === 'web' ? 'Standalone browser — login required' : 'Native — login required',
      'POST /api/auth/login',
    );
  // loadPage is stable — intentionally omitted from deps
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Login (credentials mode) ───────────────────────────────────────────────
  const handleLogin = useCallback(async () => {
    if (!loginUser.trim() || !loginPass.trim()) {
      setLoginError('Username and password are required.');
      return;
    }
    setLoginError('');
    setLoginLoading(true);
    addLog('POST /api/auth/login', loginUser);
    try {
      const res = await apiFetch('/api/auth/login', {
        method: 'POST',
        body:   JSON.stringify({ username: loginUser.trim(), password: loginPass }),
        headers: { 'Content-Type': 'application/json' },
      });

      addLog(`POST /api/auth/login → ${res.status}`);

      if (res.status === 401) {
        setLoginError('Invalid username or password.');
        return;
      }
      if (res.status === 429) {
        setLoginError('Too many attempts. Please wait a moment and try again.');
        return;
      }
      if (!res.ok) {
        const body = await res.json().catch(() => null) as { error_description?: string } | null;
        setLoginError(body?.error_description ?? `Login failed (HTTP ${res.status})`);
        return;
      }

      const json = (await res.json()) as { access_token: string };
      await acceptToken(json.access_token, 'login');
      setTokenSource('login');
      await loadPage(1);
    } catch (e) {
      addLog('Login error', String(e));
      setLoginError(`Could not reach the API: ${String(e)}`);
    } finally {
      setLoginLoading(false);
    }
  // loadPage is stable — intentionally omitted from deps
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loginUser, loginPass, acceptToken, addLog]);

  // ── Data fetching ──────────────────────────────────────────────────────────
  const loadPage = useCallback(async (pageNum: number) => {
    setLoading(true);
    const path = `/api/customers?page=${pageNum}&pageSize=${PAGE_SIZE}`;
    addLog(`GET ${path}`);
    try {
      const t0  = Date.now();
      const res = await apiFetch(path);
      addLog(`GET /api/customers → ${res.status}`, `${Date.now() - t0}ms`);

      if (res.status === 401) { setStatus('API rejected token — 401 Unauthorized.'); return; }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);

      const data = (await res.json()) as PagedResult;
      setCustomers(data.items);
      setPage(data.page);
      setTotalPages(data.totalPages);
      setStatus(
        `${data.totalCount} customer${data.totalCount !== 1 ? 's' : ''}` +
        (data.totalPages > 1 ? ` · page ${data.page} of ${data.totalPages}` : ''),
      );
    } catch (e) {
      addLog('loadPage error', String(e));
      setStatus(`Load failed: ${String(e)}`);
    } finally {
      setLoading(false);
    }
  }, [addLog]);

  // ── CRUD actions ───────────────────────────────────────────────────────────
  const openCreate = () => {
    setEditingCustomer(null); setForm(EMPTY_FORM); setFormError(''); setShowForm(true);
  };
  const openEdit = (c: Customer) => {
    setEditingCustomer(c);
    setForm({ name: c.name, email: c.email, totalSpend: String(c.totalSpend) });
    setFormError('');
    setShowForm(true);
  };

  const submitForm = async () => {
    if (!form.name.trim())  { setFormError('Name is required.');  return; }
    if (!form.email.trim()) { setFormError('Email is required.'); return; }
    const spend = parseFloat(form.totalSpend || '0');
    if (isNaN(spend) || spend < 0) { setFormError('Total Spend must be a number ≥ 0.'); return; }

    setFormError('');
    setSaving(true);
    const body   = JSON.stringify({ name: form.name.trim(), email: form.email.trim(), totalSpend: spend });
    const method = editingCustomer ? 'PUT' : 'POST';
    const path   = editingCustomer
      ? `/api/customers/${editingCustomer.customerId}`
      : `/api/customers`;

    addLog(`${method} ${path}`, body);
    try {
      const t0  = Date.now();
      const res = await apiFetch(path, {
        method,
        body,
        headers: { 'Content-Type': 'application/json' },
      });
      addLog(`${method} /api/customers → ${res.status}`, `${Date.now() - t0}ms`);

      if (!res.ok) {
        const err = await res.json().catch(() => null) as { detail?: string; title?: string } | null;
        setFormError(err?.detail ?? err?.title ?? `Server error ${res.status}`);
        return;
      }
      setShowForm(false);
      await loadPage(editingCustomer ? page : 1);
    } catch (e) {
      addLog('submitForm error', String(e));
      setFormError(String(e));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = (customer: Customer) => {
    const doDelete = async () => {
      setLoading(true);
      const path = `/api/customers/${customer.customerId}`;
      addLog(`DELETE ${path}`);
      try {
        const t0  = Date.now();
        const res = await apiFetch(path, { method: 'DELETE' });
        addLog(`DELETE /api/customers/${customer.customerId} → ${res.status}`, `${Date.now() - t0}ms`);
        const next = customers.length === 1 && page > 1 ? page - 1 : page;
        await loadPage(next);
      } catch (e) {
        addLog('delete error', String(e));
        setStatus(`Delete failed: ${String(e)}`);
      } finally {
        setLoading(false);
      }
    };

    if (Platform.OS === 'web') {
      if (window.confirm(`Delete "${customer.name}"?`)) doDelete();
    } else {
      Alert.alert('Delete Customer', `Delete "${customer.name}"?`, [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Delete', style: 'destructive', onPress: doDelete },
      ]);
    }
  };

  // ── Login screen (credentials mode, not yet authenticated) ─────────────────
  if (authMode === 'credentials' && !rawToken) {
    return (
      <KeyboardAvoidingView
        style={styles.loginContainer}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <View style={styles.loginCard}>
          <Text style={styles.loginTitle}>PoC Migration</Text>
          <Text style={styles.loginSubtitle}>Sign in to continue</Text>

          <Text style={styles.label}>Username</Text>
          <TextInput
            style={styles.input}
            value={loginUser}
            onChangeText={setLoginUser}
            placeholder="your.username"
            autoCapitalize="none"
            autoCorrect={false}
            returnKeyType="next"
          />

          <Text style={styles.label}>Password</Text>
          <TextInput
            style={styles.input}
            value={loginPass}
            onChangeText={setLoginPass}
            placeholder="••••••••"
            secureTextEntry
            returnKeyType="go"
            onSubmitEditing={handleLogin}
          />

          {loginError ? <Text style={styles.formError}>{loginError}</Text> : null}

          <Pressable
            style={[styles.submitBtn, loginLoading && styles.submitBtnOff]}
            onPress={handleLogin}
            disabled={loginLoading}
          >
            {loginLoading
              ? <ActivityIndicator color="#fff" />
              : <Text style={styles.submitBtnText}>Sign in</Text>
            }
          </Pressable>

          <Text style={styles.loginHint}>API: {API_BASE}</Text>
        </View>
      </KeyboardAvoidingView>
    );
  }

  // ── Iframe waiting screen ──────────────────────────────────────────────────
  if (authMode === 'iframe' && !rawToken) {
    return (
      <View style={[styles.loginContainer, { justifyContent: 'center', alignItems: 'center' }]}>
        <ActivityIndicator size="large" color="#1a3c5e" />
        <Text style={[styles.loginSubtitle, { marginTop: 16, color: '#666' }]}>
          Waiting for authentication from shell…
        </Text>
      </View>
    );
  }

  // ── Render (authenticated) ─────────────────────────────────────────────────
  const payload = jwtParts?.payload;

  return (
    <View style={styles.container}>
      <StatusBar style="auto" />

      {/* Header */}
      <View style={styles.header}>
        <View style={styles.titleRow}>
          <Text style={styles.title}>PoC Migration — Customers</Text>
          <View style={{ flexDirection: 'row', gap: 6, flexWrap: 'wrap' }}>
            {tokenSource && (
              <View style={[styles.badge, badgeColor[tokenSource]]}>
                <Text style={styles.badgeText}>
                  {tokenSource === 'iframe' ? '⬡ Shell iframe' : '◉ Signed in'}
                </Text>
              </View>
            )}
            <Pressable
              style={[styles.badge, { backgroundColor: showDebug ? '#c0392b' : '#37474f' }]}
              onPress={() => setShowDebug(d => !d)}
            >
              <Text style={styles.badgeText}>{showDebug ? '✕ Hide Debug' : '⚙ Debug'}</Text>
            </Pressable>
          </View>
        </View>
        <Text style={styles.status}>{status}</Text>
      </View>

      {/* Body — table + optional debug panel side-by-side on wide screens */}
      <View style={{ flex: 1, flexDirection: 'row', gap: 10 }}>

        {/* Customer table */}
        <View style={[styles.card, { flex: showDebug ? 1 : 2 }]}>
          <View style={styles.toolbar}>
            <Text style={styles.cardTitle}>Customers</Text>
            <Pressable style={styles.addBtn} onPress={openCreate}>
              <Text style={styles.addBtnText}>+ Add</Text>
            </Pressable>
          </View>

          <View style={[styles.row, styles.headerRow]}>
            <Text style={[styles.cell, styles.hCell, { flex: 2 }]}>Name</Text>
            <Text style={[styles.cell, styles.hCell, { flex: 3 }]}>Email</Text>
            <Text style={[styles.cell, styles.hCell, { flex: 1 }]}>Tier</Text>
            <Text style={[styles.cell, styles.hCell, { flex: 1 }]}>Spend</Text>
            <Text style={[styles.cell, styles.hCell, { flex: 1 }]}></Text>
          </View>

          {loading && customers.length === 0 ? (
            <ActivityIndicator style={{ margin: 24 }} color="#1a3c5e" />
          ) : (
            <FlatList
              data={customers}
              keyExtractor={item => String(item.customerId)}
              renderItem={({ item }) => (
                <View style={[styles.row, styles.dataRow]}>
                  <Text style={[styles.cell, { flex: 2 }]} numberOfLines={1}>{item.name}</Text>
                  <Text style={[styles.cell, { flex: 3 }]} numberOfLines={1}>{item.email}</Text>
                  <Text style={[styles.cell, { flex: 1, color: TIER_COLORS[item.tier] ?? '#000', fontWeight: 'bold' }]}>
                    {item.tier}
                  </Text>
                  <Text style={[styles.cell, { flex: 1 }]}>${item.totalSpend.toFixed(2)}</Text>
                  <View style={[styles.actions, { flex: 1 }]}>
                    <Pressable style={styles.editBtn} onPress={() => openEdit(item)}>
                      <Text style={styles.editBtnText}>Edit</Text>
                    </Pressable>
                    <Pressable style={styles.delBtn} onPress={() => handleDelete(item)}>
                      <Text style={styles.delBtnText}>Del</Text>
                    </Pressable>
                  </View>
                </View>
              )}
            />
          )}

          {totalPages > 1 && (
            <View style={styles.pagination}>
              <Pressable
                style={[styles.pageBtn, page <= 1 && styles.pageBtnOff]}
                disabled={page <= 1}
                onPress={() => loadPage(page - 1)}
              >
                <Text style={styles.pageBtnText}>‹ Prev</Text>
              </Pressable>
              <Text style={styles.pageInfo}>Page {page} of {totalPages}</Text>
              <Pressable
                style={[styles.pageBtn, page >= totalPages && styles.pageBtnOff]}
                disabled={page >= totalPages}
                onPress={() => loadPage(page + 1)}
              >
                <Text style={styles.pageBtnText}>Next ›</Text>
              </Pressable>
            </View>
          )}
        </View>

        {/* ── Debug Panel ────────────────────────────────────────────────── */}
        {showDebug && (
          <ScrollView style={styles.debugPanel}>

            <Text style={styles.dbgSection}>Authentication</Text>
            <View style={styles.dbgRow}>
              <Text style={styles.dbgKey}>Platform</Text>
              <Text style={styles.dbgVal}>{Platform.OS}</Text>
            </View>
            <View style={styles.dbgRow}>
              <Text style={styles.dbgKey}>Auth mode</Text>
              <Text style={styles.dbgVal}>{authMode ?? '(pending)'}</Text>
            </View>
            <View style={styles.dbgRow}>
              <Text style={styles.dbgKey}>Token source</Text>
              <Text style={styles.dbgVal}>{tokenSource ?? '(pending)'}</Text>
            </View>
            {Platform.OS === 'web' && window.parent !== window && (
              <View style={styles.dbgRow}>
                <Text style={styles.dbgKey}>Trusted origin</Text>
                <Text style={styles.dbgVal}>{SHELL_ORIGIN.join(', ')}</Text>
              </View>
            )}
            <View style={styles.dbgRow}>
              <Text style={styles.dbgKey}>API base</Text>
              <Text style={styles.dbgVal}>{API_BASE}</Text>
            </View>
            <View style={styles.dbgRow}>
              <Text style={styles.dbgKey}>API key</Text>
              <Text style={styles.dbgVal}>mobile-app-v1</Text>
            </View>

            <Text style={[styles.dbgSection, { marginTop: 12 }]}>Raw JWT</Text>
            {rawToken ? (
              <Text style={styles.dbgMono} selectable>{rawToken}</Text>
            ) : (
              <Text style={styles.dbgDim}>No token yet</Text>
            )}

            {jwtParts?.header && (
              <>
                <Text style={[styles.dbgSection, { marginTop: 12 }]}>JWT Header</Text>
                {Object.entries(jwtParts.header).map(([k, v]) => (
                  <View key={k} style={styles.dbgRow}>
                    <Text style={styles.dbgKey}>{k}</Text>
                    <Text style={styles.dbgVal}>{String(v)}</Text>
                  </View>
                ))}
              </>
            )}

            {payload && (
              <>
                <Text style={[styles.dbgSection, { marginTop: 12 }]}>JWT Claims</Text>
                {Object.entries(payload).map(([k, v]) => {
                  let display = String(v);
                  if (k === 'exp') display = fmtUnix(v) + expCountdown(v);
                  else if (k === 'iat' || k === 'nbf') display = fmtUnix(v);
                  return (
                    <View key={k} style={styles.dbgRow}>
                      <Text style={styles.dbgKey}>{k}</Text>
                      <Text style={[styles.dbgVal, k === 'exp' && typeof v === 'number' && v * 1000 < Date.now() ? { color: '#e57373' } : null]}>
                        {display}
                      </Text>
                    </View>
                  );
                })}
              </>
            )}

            <Text style={[styles.dbgSection, { marginTop: 12 }]}>Event Log</Text>
            {log.length === 0 && <Text style={styles.dbgDim}>No events yet</Text>}
            {log.map((entry, i) => (
              <View key={i} style={styles.logEntry}>
                <Text style={styles.logTs}>{entry.ts}</Text>
                <View style={{ flex: 1 }}>
                  <Text style={styles.logMsg}>{entry.msg}</Text>
                  {entry.detail && <Text style={styles.logDetail}>{entry.detail}</Text>}
                </View>
              </View>
            ))}
          </ScrollView>
        )}
      </View>

      {/* Create / Edit modal */}
      <Modal visible={showForm} animationType="fade" transparent>
        <View style={styles.overlay}>
          <View style={styles.modalCard}>
            <Text style={styles.modalTitle}>
              {editingCustomer ? 'Edit Customer' : 'New Customer'}
            </Text>

            <Text style={styles.label}>Name *</Text>
            <TextInput
              style={styles.input}
              value={form.name}
              onChangeText={v => setForm(f => ({ ...f, name: v }))}
              placeholder="Alice Chen"
              autoCapitalize="words"
            />

            <Text style={styles.label}>Email *</Text>
            <TextInput
              style={styles.input}
              value={form.email}
              onChangeText={v => setForm(f => ({ ...f, email: v }))}
              placeholder="alice@example.com"
              keyboardType="email-address"
              autoCapitalize="none"
            />

            <Text style={styles.label}>Total Spend (USD)</Text>
            <TextInput
              style={styles.input}
              value={form.totalSpend}
              onChangeText={v => setForm(f => ({ ...f, totalSpend: v }))}
              placeholder="0.00"
              keyboardType="decimal-pad"
            />

            {formError ? <Text style={styles.formError}>{formError}</Text> : null}

            <View style={styles.modalActions}>
              <Pressable style={styles.cancelBtn} onPress={() => setShowForm(false)}>
                <Text style={styles.cancelBtnText}>Cancel</Text>
              </Pressable>
              <Pressable
                style={[styles.submitBtn, saving && styles.submitBtnOff]}
                disabled={saving}
                onPress={submitForm}
              >
                <Text style={styles.submitBtnText}>{saving ? 'Saving…' : 'Save'}</Text>
              </Pressable>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
}

// ── Styles ────────────────────────────────────────────────────────────────────
const styles = StyleSheet.create({
  // Login screen
  loginContainer: {
    flex: 1,
    backgroundColor: '#f0f2f5',
    justifyContent: 'center',
    padding: 24,
  },
  loginCard: {
    backgroundColor: '#fff',
    borderRadius: 10,
    padding: 28,
    maxWidth: 400,
    width: '100%',
    alignSelf: 'center',
    borderWidth: 1,
    borderColor: '#dde1e7',
    boxShadow: '0 4px 12px rgba(0,0,0,0.08)',
    elevation: 4,
  },
  loginTitle: {
    fontSize: 22,
    fontWeight: 'bold',
    color: '#1a3c5e',
    marginBottom: 4,
  },
  loginSubtitle: {
    fontSize: 14,
    color: '#888',
    marginBottom: 24,
  },
  loginHint: {
    fontSize: 11,
    color: '#aaa',
    textAlign: 'center',
    marginTop: 16,
    fontFamily: 'monospace',
  },

  // Main layout
  container: {
    flex: 1,
    backgroundColor: '#f0f2f5',
    paddingTop: 48,
    paddingHorizontal: 16,
    paddingBottom: 16,
  },
  header:    { marginBottom: 12 },
  titleRow:  { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: 8, marginBottom: 4 },
  title:     { fontSize: 20, fontWeight: 'bold', color: '#1a3c5e' },
  badge:     { paddingHorizontal: 8, paddingVertical: 3, borderRadius: 12 },
  badgeText: { fontSize: 11, fontWeight: '600', color: '#fff' },
  status:    { fontSize: 13, color: '#666', fontStyle: 'italic' },

  // Card / table
  card: {
    flex: 1,
    backgroundColor: '#fff',
    borderRadius: 6,
    borderWidth: 1,
    borderColor: '#dde1e7',
    overflow: 'hidden',
  },
  toolbar: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 12,
    paddingVertical: 10,
    borderBottomWidth: 1,
    borderBottomColor: '#eee',
  },
  cardTitle: { fontSize: 15, fontWeight: '700', color: '#1a3c5e' },

  addBtn:     { backgroundColor: '#1a3c5e', paddingHorizontal: 14, paddingVertical: 6, borderRadius: 4 },
  addBtnText: { color: '#fff', fontWeight: '700', fontSize: 13 },

  row:       { flexDirection: 'row', paddingVertical: 8, paddingHorizontal: 12, alignItems: 'center' },
  headerRow: { backgroundColor: '#1a3c5e' },
  dataRow:   { borderTopWidth: 1, borderTopColor: '#f0f2f5' },
  cell:      { fontSize: 13, color: '#333', paddingRight: 6 },
  hCell:     { color: '#fff', fontWeight: '700' },

  actions:     { flexDirection: 'row', gap: 4 },
  editBtn:     { backgroundColor: '#4a90d9', paddingHorizontal: 7, paddingVertical: 3, borderRadius: 3 },
  editBtnText: { color: '#fff', fontSize: 11, fontWeight: '600' },
  delBtn:      { backgroundColor: '#d94a4a', paddingHorizontal: 7, paddingVertical: 3, borderRadius: 3 },
  delBtnText:  { color: '#fff', fontSize: 11, fontWeight: '600' },

  pagination: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 10,
    gap: 16,
    borderTopWidth: 1,
    borderTopColor: '#eee',
  },
  pageBtn:     { backgroundColor: '#1a3c5e', paddingHorizontal: 14, paddingVertical: 6, borderRadius: 4 },
  pageBtnOff:  { backgroundColor: '#c5c9d0' },
  pageBtnText: { color: '#fff', fontSize: 13 },
  pageInfo:    { fontSize: 13, color: '#555' },

  // Shared form elements (login + modals)
  label: { fontSize: 13, fontWeight: '600', color: '#444', marginBottom: 4 },
  input: {
    borderWidth: 1,
    borderColor: '#ccc',
    borderRadius: 4,
    paddingHorizontal: 10,
    paddingVertical: 8,
    fontSize: 14,
    marginBottom: 12,
    backgroundColor: '#fafafa',
  },
  formError: { color: '#c0392b', fontSize: 13, marginBottom: 10 },

  // Modal
  overlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.45)',
    justifyContent: 'center',
    alignItems: 'center',
  },
  modalCard: {
    backgroundColor: '#fff',
    borderRadius: 8,
    padding: 24,
    width: '90%',
    maxWidth: 460,
    boxShadow: '0 4px 10px rgba(0,0,0,0.25)',
    elevation: 8,
  },
  modalTitle:    { fontSize: 17, fontWeight: '700', color: '#1a3c5e', marginBottom: 16 },
  modalActions:  { flexDirection: 'row', justifyContent: 'flex-end', gap: 10, marginTop: 4 },
  cancelBtn:     { paddingHorizontal: 16, paddingVertical: 10, borderRadius: 4, borderWidth: 1, borderColor: '#ccc' },
  cancelBtnText: { color: '#555', fontSize: 14 },
  submitBtn:     { backgroundColor: '#1a3c5e', paddingHorizontal: 20, paddingVertical: 10, borderRadius: 4 },
  submitBtnOff:  { backgroundColor: '#a0aab5' },
  submitBtnText: { color: '#fff', fontWeight: '700', fontSize: 14 },

  // Debug panel
  debugPanel: {
    flex: 1,
    backgroundColor: '#1e1e2e',
    borderRadius: 6,
    borderWidth: 1,
    borderColor: '#3a3a5c',
    padding: 12,
  },
  dbgSection: {
    fontSize: 11,
    fontWeight: '700',
    color: '#7c8cf8',
    textTransform: 'uppercase',
    letterSpacing: 0.8,
    marginBottom: 4,
  },
  dbgRow: {
    flexDirection: 'row',
    paddingVertical: 3,
    borderBottomWidth: 1,
    borderBottomColor: '#2a2a40',
    gap: 8,
  },
  dbgKey: {
    width: 110,
    fontSize: 11,
    color: '#9cdcfe',
    fontFamily: 'monospace',
    flexShrink: 0,
  },
  dbgVal: {
    flex: 1,
    fontSize: 11,
    color: '#ce9178',
    fontFamily: 'monospace',
  },
  dbgMono: {
    fontSize: 10,
    color: '#4ec9b0',
    fontFamily: 'monospace',
    backgroundColor: '#252540',
    padding: 6,
    borderRadius: 4,
  },
  dbgDim: {
    fontSize: 11,
    color: '#555577',
    fontStyle: 'italic',
  },

  // Event log
  logEntry: {
    flexDirection: 'row',
    gap: 8,
    paddingVertical: 3,
    borderBottomWidth: 1,
    borderBottomColor: '#2a2a40',
  },
  logTs:     { fontSize: 10, color: '#5a5a7a', fontFamily: 'monospace', width: 84, flexShrink: 0 },
  logMsg:    { fontSize: 11, color: '#d4d4d4', fontFamily: 'monospace' },
  logDetail: { fontSize: 10, color: '#6a9955', fontFamily: 'monospace', marginTop: 1 },
});
