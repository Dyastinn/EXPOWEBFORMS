import { StatusBar } from 'expo-status-bar';
import { useCallback, useEffect, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  FlatList,
  Modal,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';

// ── Config ────────────────────────────────────────────────────────────────────
let inMemoryToken: string | null = null;

const API_URL   = 'http://localhost:5050';
const SHELL_SIM = 'http://localhost:5000';
const PAGE_SIZE = 5;

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

type FormState = { name: string; email: string; totalSpend: string };

// ── Constants ─────────────────────────────────────────────────────────────────
const TIER_COLORS: Record<string, string> = {
  Gold:   '#B8860B',
  Silver: '#708090',
  Bronze: '#8B4513',
};

const EMPTY_FORM: FormState = { name: '', email: '', totalSpend: '' };

const badgeColor: Record<TokenSource, { backgroundColor: string }> = {
  iframe:     { backgroundColor: '#4a3fa0' },  // purple     — parent-managed token
  standalone: { backgroundColor: '#2e7d4f' },  // green      — direct tab, self-generated
  mobile:     { backgroundColor: '#1a6b3c' },  // dark green — native, self-generated
};

// 'iframe'    = inside ANY iframe → parent must send AUTH_TOKEN via postMessage
// 'standalone'= direct browser tab → self-generate token
// 'mobile'    = native app         → self-generate token
type TokenSource = 'iframe' | 'standalone' | 'mobile';

// ── Component ─────────────────────────────────────────────────────────────────
export default function App() {
  const [customers,   setCustomers]   = useState<Customer[]>([]);
  const [page,       setPage]       = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [status,     setStatus]     = useState('Waiting for authentication…');
  const [loading,     setLoading]     = useState(false);
  const [tokenSource, setTokenSource] = useState<TokenSource | null>(null);

  // Form modal state
  const [showForm,        setShowForm]        = useState(false);
  const [editingCustomer, setEditingCustomer] = useState<Customer | null>(null);
  const [form,            setForm]            = useState<FormState>(EMPTY_FORM);
  const [formError,       setFormError]       = useState('');
  const [saving,          setSaving]          = useState(false);

  // ── Auth setup ─────────────────────────────────────────────────────────────
  useEffect(() => {
    const fetchToken = async () => {
      setStatus('Fetching token…');
      try {
        const res  = await fetch(`${SHELL_SIM}/token`, { method: 'POST' });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const json = (await res.json()) as { access_token: string };
        inMemoryToken = json.access_token;
        await loadPage(1);
      } catch (e) {
        setStatus(`Token fetch failed: ${String(e)}`);
      }
    };

    if (Platform.OS === 'web') {
      if (window.parent !== window) {
        // Embedded in an iframe — wait for the shell to postMessage the token.
        setStatus('Waiting for token from shell…');
        const onMessage = (event: MessageEvent) => {
          if (event.source !== window.parent) return;
          if (event.origin !== 'http://localhost:5000') return;
          const msg = event.data as { type?: string; token?: string };
          if (msg?.type !== 'AUTH_TOKEN' || !msg.token) return;
          window.removeEventListener('message', onMessage);
          inMemoryToken = msg.token;
          setTokenSource('iframe');
          loadPage(1);
        };
        window.addEventListener('message', onMessage);
        return () => window.removeEventListener('message', onMessage);
      }
      // Direct browser tab — self-generate.
      setTokenSource('standalone');
      fetchToken();
      return;
    }

    setTokenSource('mobile');
    fetchToken();
  // loadPage is stable after mount — intentionally omitted from deps
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Data fetching ──────────────────────────────────────────────────────────
  const loadPage = useCallback(async (pageNum: number) => {
    if (!inMemoryToken) return;
    setLoading(true);
    try {
      const res = await fetch(
        `${API_URL}/api/customers?page=${pageNum}&pageSize=${PAGE_SIZE}`,
        { headers: { Authorization: `Bearer ${inMemoryToken}` } },
      );
      if (res.status === 401) {
        setStatus('API rejected token — 401 Unauthorized.');
        return;
      }
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
      setStatus(`Load failed: ${String(e)}`);
    } finally {
      setLoading(false);
    }
  }, []);

  // ── CRUD actions ───────────────────────────────────────────────────────────
  const openCreate = () => {
    setEditingCustomer(null);
    setForm(EMPTY_FORM);
    setFormError('');
    setShowForm(true);
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
    if (isNaN(spend) || spend < 0) {
      setFormError('Total Spend must be a number ≥ 0.');
      return;
    }

    setFormError('');
    setSaving(true);
    const body    = JSON.stringify({ name: form.name.trim(), email: form.email.trim(), totalSpend: spend });
    const headers = { Authorization: `Bearer ${inMemoryToken}`, 'Content-Type': 'application/json' };

    try {
      const res = editingCustomer
        ? await fetch(`${API_URL}/api/customers/${editingCustomer.customerId}`, { method: 'PUT',  headers, body })
        : await fetch(`${API_URL}/api/customers`,                                { method: 'POST', headers, body });

      if (!res.ok) {
        const err = await res.json().catch(() => null) as { detail?: string; title?: string } | null;
        setFormError(err?.detail ?? err?.title ?? `Server error ${res.status}`);
        return;
      }

      setShowForm(false);
      // After create, go to page 1 (sorted by spend — customer may land anywhere).
      // After update, stay on current page.
      await loadPage(editingCustomer ? page : 1);
    } catch (e) {
      setFormError(String(e));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = (customer: Customer) => {
    const doDelete = async () => {
      setLoading(true);
      try {
        await fetch(`${API_URL}/api/customers/${customer.customerId}`, {
          method:  'DELETE',
          headers: { Authorization: `Bearer ${inMemoryToken}` },
        });
        // If deleting the last row on a page past page 1, go back one page.
        const next = customers.length === 1 && page > 1 ? page - 1 : page;
        await loadPage(next);
      } catch (e) {
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

  // ── Render ─────────────────────────────────────────────────────────────────
  return (
    <View style={styles.container}>
      <StatusBar style="auto" />

      {/* Header */}
      <View style={styles.header}>
        <View style={styles.titleRow}>
          <Text style={styles.title}>PoC Migration — Customers</Text>
          {tokenSource && (
            <View style={[styles.badge, badgeColor[tokenSource]]}>
              <Text style={styles.badgeText}>
                {tokenSource === 'iframe'     && '⬡ Iframe — token required from parent'}
                {tokenSource === 'standalone' && '◎ Standalone — token self-generated'}
                {tokenSource === 'mobile'     && '◉ Mobile — token self-generated'}
              </Text>
            </View>
          )}
        </View>
        <Text style={styles.status}>{status}</Text>
      </View>

      {/* Table card */}
      <View style={styles.card}>

        {/* Toolbar */}
        <View style={styles.toolbar}>
          <Text style={styles.cardTitle}>Customers</Text>
          <Pressable style={styles.addBtn} onPress={openCreate}>
            <Text style={styles.addBtnText}>+ Add</Text>
          </Pressable>
        </View>

        {/* Column headers */}
        <View style={[styles.row, styles.headerRow]}>
          <Text style={[styles.cell, styles.hCell, { flex: 2 }]}>Name</Text>
          <Text style={[styles.cell, styles.hCell, { flex: 3 }]}>Email</Text>
          <Text style={[styles.cell, styles.hCell, { flex: 1 }]}>Tier</Text>
          <Text style={[styles.cell, styles.hCell, { flex: 1 }]}>Spend</Text>
          <Text style={[styles.cell, styles.hCell, { flex: 1 }]}></Text>
        </View>

        {/* Rows */}
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

        {/* Pagination */}
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
  container: {
    flex: 1,
    backgroundColor: '#f0f2f5',
    paddingTop: 48,
    paddingHorizontal: 16,
  },

  // Header
  header:    { marginBottom: 16 },
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

  actions:    { flexDirection: 'row', gap: 4 },
  editBtn:    { backgroundColor: '#4a90d9', paddingHorizontal: 7, paddingVertical: 3, borderRadius: 3 },
  editBtnText:{ color: '#fff', fontSize: 11, fontWeight: '600' },
  delBtn:     { backgroundColor: '#d94a4a', paddingHorizontal: 7, paddingVertical: 3, borderRadius: 3 },
  delBtnText: { color: '#fff', fontSize: 11, fontWeight: '600' },

  // Pagination
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
  modalTitle:   { fontSize: 17, fontWeight: '700', color: '#1a3c5e', marginBottom: 16 },
  label:        { fontSize: 13, fontWeight: '600', color: '#444', marginBottom: 4 },
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
  formError:    { color: '#c0392b', fontSize: 13, marginBottom: 10 },
  modalActions: { flexDirection: 'row', justifyContent: 'flex-end', gap: 10, marginTop: 4 },

  cancelBtn:     { paddingHorizontal: 16, paddingVertical: 10, borderRadius: 4, borderWidth: 1, borderColor: '#ccc' },
  cancelBtnText: { color: '#555', fontSize: 14 },
  submitBtn:     { backgroundColor: '#1a3c5e', paddingHorizontal: 20, paddingVertical: 10, borderRadius: 4 },
  submitBtnOff:  { backgroundColor: '#a0aab5' },
  submitBtnText: { color: '#fff', fontWeight: '700', fontSize: 14 },
});
