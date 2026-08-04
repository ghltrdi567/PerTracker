import { useEffect, useState, type FormEvent } from 'react';
import {
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  Tab,
  Tabs,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import {
  Add as AddIcon,
  Delete as DeleteIcon,
  Person as PersonIcon,
  Event as EventIcon,
  Search as SearchIcon,
  Visibility as ViewIcon,
  Security as AdminIcon,
  Category as CategoryIcon,
} from '@mui/icons-material';
import { getAdminUsers, deleteAdminUser, getAdminEvents, deleteAdminEvent } from '../api/admin';
import type { AdminUser, AdminEvent } from '../api/admin';
import { getCategories, createCategory, deleteCategory } from '../api/categories';
import type { Category } from '../types/api';
import { getApiErrors, formatDate } from '../utils/helpers';
import { useToast } from '../components/ToastProvider';
import { useNavigate } from 'react-router-dom';

type TabValue = 'users' | 'events' | 'categories';

const PAGE_SIZE = 20;

/**
 * Страница админ-панели.
 *
 * Три вкладки (Tab):
 * 1. "Пользователи" — поиск, просмотр, удаление пользователей.
 * 2. "События" — просмотр и удаление любых событий.
 * 3. "Категории" — создание и удаление категорий событий.
 *
 * Поиск пользователей:
 * - searchInput — текущее значение текстового поля (controlled input).
 * - searchQuery — значение, которое уже было отправлено на сервер.
 * - Разделение нужно, чтобы loadUsers() не зависел от асинхронного setState.
 * - Проблема: если вызывать setSearchQuery(searchInput), а потом loadUsers(),
 *   loadUsers() читает searchQuery из замыкания — ещё старое значение.
 * - Решение: loadUsers() принимает опциональный параметр searchOverride,
 *   который передаётся напрямую, минуя состояние.
 *
 * Пагинация (Load More):
 * - GET-запросы возвращают PagedResult с totalCount.
 * - Если items.length < totalCount, показываем кнопку "Загрузить ещё".
 * - append = true добавляет результаты к текущему списку, не заменяя его.
 *
 * Удаление:
 * - Все три типа (user/event/category) используют один Dialog.
 * - deleteDialog.type определяет, какой API вызывать и какой текст показать.
 */
export default function AdminDashboardPage() {
  const toast = useToast();
  const navigate = useNavigate();

  const [tab, setTab] = useState<TabValue>('users');

  // Users state
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [usersLoading, setUsersLoading] = useState(true);
  const [usersPage, setUsersPage] = useState(1);
  const [usersTotal, setUsersTotal] = useState(0);
  const [searchQuery, setSearchQuery] = useState('');
  const [searchInput, setSearchInput] = useState('');

  // Events state
  const [events, setEvents] = useState<AdminEvent[]>([]);
  const [eventsLoading, setEventsLoading] = useState(true);
  const [eventsPage, setEventsPage] = useState(1);
  const [eventsTotal, setEventsTotal] = useState(0);

  // Categories state
  const [categories, setCategories] = useState<Category[]>([]);
  const [categoriesLoading, setCategoriesLoading] = useState(true);
  const [catName, setCatName] = useState('');
  const [catIcon, setCatIcon] = useState('');
  const [catSaving, setCatSaving] = useState(false);

  // Delete dialog
  const [deleteDialog, setDeleteDialog] = useState<{
    type: 'user' | 'event' | 'category';
    id: string;
    label: string;
  } | null>(null);
  const [deleting, setDeleting] = useState(false);

  // Load users — searchOverride fixes the stale-closure bug on first search
  const loadUsers = async (page: number, append = false, searchOverride?: string) => {
    const search = searchOverride ?? searchQuery;
    if (!append) setUsersLoading(true);
    try {
      const res = await getAdminUsers(page, PAGE_SIZE, search || undefined);
      if (append) {
        setUsers((prev) => [...prev, ...res.items]);
      } else {
        setUsers(res.items);
      }
      setUsersPage(page);
      setUsersTotal(res.totalCount);
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    } finally {
      setUsersLoading(false);
    }
  };

  // Load events
  const loadEvents = async (page: number, append = false) => {
    if (!append) setEventsLoading(true);
    try {
      const res = await getAdminEvents(page, PAGE_SIZE);
      if (append) {
        setEvents((prev) => [...prev, ...res.items]);
      } else {
        setEvents(res.items);
      }
      setEventsPage(page);
      setEventsTotal(res.totalCount);
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    } finally {
      setEventsLoading(false);
    }
  };

  // Load categories
  const loadCategories = async () => {
    setCategoriesLoading(true);
    try {
      const res = await getCategories(1, 100);
      setCategories(res?.items ?? []);
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    } finally {
      setCategoriesLoading(false);
    }
  };

  useEffect(() => {
    if (tab === 'users') loadUsers(1);
    else if (tab === 'events') loadEvents(1);
    else loadCategories();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab]);

  // FIX: pass searchInput directly as searchOverride — setState is async
  const handleSearch = (e: FormEvent) => {
    e.preventDefault();
    setSearchQuery(searchInput);
    loadUsers(1, false, searchInput);
  };

  const handleResetSearch = () => {
    setSearchQuery('');
    setSearchInput('');
    loadUsers(1, false, '');
  };

  const handleCreateCategory = async (e: FormEvent) => {
    e.preventDefault();
    setCatSaving(true);
    try {
      await createCategory({ name: catName, icon: catIcon || null });
      toast.show('Категория создана');
      setCatName('');
      setCatIcon('');
      await loadCategories();
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    } finally {
      setCatSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteDialog) return;
    setDeleting(true);
    try {
      if (deleteDialog.type === 'user') {
        await deleteAdminUser(deleteDialog.id);
        toast.show(`Пользователь "${deleteDialog.label}" удалён`);
        setUsers((prev) => prev.filter((u) => u.id !== deleteDialog.id));
      } else if (deleteDialog.type === 'event') {
        await deleteAdminEvent(deleteDialog.id);
        toast.show(`Событие "${deleteDialog.label}" удалено`);
        setEvents((prev) => prev.filter((e) => e.id !== deleteDialog.id));
      } else {
        await deleteCategory(deleteDialog.id);
        toast.show(`Категория "${deleteDialog.label}" удалена`);
        setCategories((prev) => prev.filter((c) => c.id !== deleteDialog.id));
      }
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    } finally {
      setDeleting(false);
      setDeleteDialog(null);
    }
  };

  const hasMoreUsers = users.length < usersTotal;
  const hasMoreEvents = events.length < eventsTotal;

  return (
    <Box>
      <Typography variant="h4" gutterBottom>
        <AdminIcon sx={{ verticalAlign: 'middle', mr: 1 }} />
        Панель администратора
      </Typography>

      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 3 }}>
        <Tab value="users" label={`Пользователи (${usersTotal})`} icon={<PersonIcon />} iconPosition="start" />
        <Tab value="events" label={`События (${eventsTotal})`} icon={<EventIcon />} iconPosition="start" />
        <Tab value="categories" label="Категории" icon={<CategoryIcon />} iconPosition="start" />
      </Tabs>

      {/* Users tab */}
      {tab === 'users' && (
        <>
          <Box component="form" onSubmit={handleSearch} sx={{ display: 'flex', gap: 1, mb: 2 }}>
            <TextField
              size="small"
              placeholder="Поиск по email или имени..."
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              sx={{ flexGrow: 1, maxWidth: 400 }}
            />
            <Button type="submit" variant="outlined" startIcon={<SearchIcon />}>
              Поиск
            </Button>
            {searchQuery && (
              <Button variant="text" onClick={handleResetSearch}>
                Сбросить
              </Button>
            )}
          </Box>

          {usersLoading ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
              <CircularProgress />
            </Box>
          ) : users.length > 0 ? (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
              {users.map((u) => (
                <Card key={u.id} variant="outlined">
                  <CardContent sx={{ py: 1.5, px: 2, '&:last-child': { pb: 1.5 } }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                      <Box sx={{ flexGrow: 1 }}>
                        <Typography variant="subtitle2">
                          {u.displayName}
                          {u.isAdmin && (
                            <Typography component="span" variant="caption" sx={{ ml: 1, color: 'primary.main' }}>
                              [ADMIN]
                            </Typography>
                          )}
                          {!u.emailConfirmed && (
                            <Typography component="span" variant="caption" sx={{ ml: 1, color: 'warning.main' }}>
                              [Не подтверждён]
                            </Typography>
                          )}
                        </Typography>
                        <Typography variant="body2" color="text.secondary">
                          {u.email} · Зарегистрирован {formatDate(u.createdAt)}
                        </Typography>
                      </Box>
                      <Tooltip title="Удалить пользователя">
                        <IconButton
                          color="error"
                          size="small"
                          onClick={() =>
                            setDeleteDialog({ type: 'user', id: u.id, label: u.displayName })
                          }
                        >
                          <DeleteIcon />
                        </IconButton>
                      </Tooltip>
                    </Box>
                  </CardContent>
                </Card>
              ))}
              {hasMoreUsers && (
                <Box sx={{ display: 'flex', justifyContent: 'center', mt: 1 }}>
                  <Button onClick={() => loadUsers(usersPage + 1, true)} disabled={usersLoading}>
                    Загрузить ещё
                  </Button>
                </Box>
              )}
            </Box>
          ) : (
            <Typography color="text.secondary">Пользователи не найдены.</Typography>
          )}
        </>
      )}

      {/* Events tab */}
      {tab === 'events' && (
        <>
          {eventsLoading ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
              <CircularProgress />
            </Box>
          ) : events.length > 0 ? (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
              {events.map((evt) => (
                <Card key={evt.id} variant="outlined">
                  <CardContent sx={{ py: 1.5, px: 2, '&:last-child': { pb: 1.5 } }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                      <Box sx={{ flexGrow: 1 }}>
                        <Typography variant="subtitle2">{evt.title}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          {evt.ownerDisplayName} ({evt.ownerEmail})
                          {evt.categoryName && ` · ${evt.categoryName}`}
                          {' · '}{formatDate(evt.createdAt)}
                        </Typography>
                      </Box>
                      <Tooltip title="Открыть событие">
                        <IconButton
                          size="small"
                          onClick={() => navigate(`/events/${evt.id}`)}
                        >
                          <ViewIcon />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Удалить событие">
                        <IconButton
                          color="error"
                          size="small"
                          onClick={() =>
                            setDeleteDialog({ type: 'event', id: evt.id, label: evt.title })
                          }
                        >
                          <DeleteIcon />
                        </IconButton>
                      </Tooltip>
                    </Box>
                  </CardContent>
                </Card>
              ))}
              {hasMoreEvents && (
                <Box sx={{ display: 'flex', justifyContent: 'center', mt: 1 }}>
                  <Button onClick={() => loadEvents(eventsPage + 1, true)} disabled={eventsLoading}>
                    Загрузить ещё
                  </Button>
                </Box>
              )}
            </Box>
          ) : (
            <Typography color="text.secondary">Событий пока нет.</Typography>
          )}
        </>
      )}

      {/* Categories tab */}
      {tab === 'categories' && (
        <>
          <Card sx={{ mb: 3 }}>
            <CardContent>
              <Typography variant="h6" gutterBottom>Создать категорию</Typography>
              <Box component="form" onSubmit={handleCreateCategory} sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
                <TextField
                  label="Название"
                  required
                  value={catName}
                  onChange={(e) => setCatName(e.target.value)}
                  inputProps={{ maxLength: 100 }}
                  size="small"
                />
                <TextField
                  label="Иконка (эмодзи)"
                  value={catIcon}
                  onChange={(e) => setCatIcon(e.target.value)}
                  inputProps={{ maxLength: 50 }}
                  size="small"
                  sx={{ width: 120 }}
                />
                <Button type="submit" variant="contained" startIcon={<AddIcon />} disabled={catSaving}>
                  {catSaving ? '...' : 'Добавить'}
                </Button>
              </Box>
            </CardContent>
          </Card>

          {categoriesLoading ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
              <CircularProgress />
            </Box>
          ) : categories.length > 0 ? (
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: '1fr 1fr 1fr', md: 'repeat(4, 1fr)' }, gap: 2 }}>
              {categories.map((cat) => (
                <Card key={cat.id}>
                  <CardContent sx={{ textAlign: 'center', position: 'relative' }}>
                    <Tooltip title="Удалить категорию">
                      <IconButton
                        size="small"
                        onClick={() => setDeleteDialog({ type: 'category', id: cat.id, label: cat.name })}
                        sx={{ position: 'absolute', top: 4, right: 4 }}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                    <Typography variant="h4">{cat.icon ?? '📁'}</Typography>
                    <Typography variant="body2">{cat.name}</Typography>
                  </CardContent>
                </Card>
              ))}
            </Box>
          ) : (
            <Typography color="text.secondary">Категорий пока нет. Создайте первую!</Typography>
          )}
        </>
      )}

      {/* Delete confirmation dialog */}
      <Dialog open={!!deleteDialog} onClose={() => !deleting && setDeleteDialog(null)}>
        <DialogTitle>Подтверждение удаления</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {deleteDialog?.type === 'user'
              ? `Вы уверены, что хотите удалить пользователя "${deleteDialog?.label}"? Все его события и данные будут безвозвратно удалены.`
              : deleteDialog?.type === 'event'
              ? `Вы уверены, что хотите удалить событие "${deleteDialog?.label}"?`
              : `Вы уверены, что хотите удалить категорию "${deleteDialog?.label}"?`}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleteDialog(null)} disabled={deleting}>
            Отмена
          </Button>
          <Button onClick={handleDelete} color="error" variant="contained" disabled={deleting}>
            {deleting ? 'Удаление...' : 'Удалить'}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}