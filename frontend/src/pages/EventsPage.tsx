import { useEffect, useState, type FormEvent } from 'react';
import {
  Box,
  Button,
  Card,
  CardActionArea,
  CardContent,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  TextField,
  Typography,
} from '@mui/material';
import { Add as AddIcon } from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import { getEvents, createEvent } from '../api/events';
import { getCategories } from '../api/categories';
import type { Category, EventResponse } from '../types/api';
import { getApiErrors, formatDate } from '../utils/helpers';
import { useToast } from '../components/ToastProvider';

const PAGE_SIZE = 20;

export default function EventsPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const [items, setItems] = useState<EventResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [hasMore, setHasMore] = useState(false);
  const [page, setPage] = useState(1);
  const [refreshKey, setRefreshKey] = useState(0);
  const [createOpen, setCreateOpen] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);

  // Поля формы создания
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [formErrors, setFormErrors] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  const loadEvents = async (pageNum: number, append: boolean) => {
    if (append) setLoadingMore(true); else setLoading(true);
    try {
      const res = await getEvents(pageNum, PAGE_SIZE);
      const newItems = res?.items ?? [];
      setItems(append ? (prev) => [...prev, ...newItems] : newItems);
      setHasMore((res?.page ?? 1) < (res?.totalPages ?? 1));
      setPage(pageNum);
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
      if (!append) setItems([]);
    } finally {
      setLoading(false);
      setLoadingMore(false);
    }
  };

  useEffect(() => {
    loadEvents(1, false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshKey]);

  // Принудительное обновление при каждом заходе на страницу
  useEffect(() => {
    setRefreshKey((k) => k + 1);
  }, []);

  const handleLoadMore = () => loadEvents(page + 1, true);

  const openCreate = async () => {
    setCreateOpen(true);
    try {
      const res = await getCategories();
      setCategories(res?.items ?? []);
    } catch {
      // ignore
    }
  };

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault();
    setFormErrors([]);
    setSaving(true);
    try {
      const event = await createEvent({
        title,
        description: description || null,
        categoryId: categoryId || null,
      });
      toast.show('Событие создано');
      setCreateOpen(false);
      setTitle('');
      setDescription('');
      setCategoryId('');
      navigate(`/events/${event.id}`);
    } catch (err) {
      setFormErrors(getApiErrors(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 3 }}>
        <Typography variant="h4">Мои события</Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={openCreate}>
          Создать
        </Button>
      </Box>

      {loading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
          <CircularProgress />
        </Box>
      ) : items.length > 0 ? (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', md: '1fr 1fr 1fr' }, gap: 2 }}>
            {items.map((event) => (
              <Card key={event.id}>
                <CardActionArea onClick={() => navigate(`/events/${event.id}`)}>
                  <CardContent>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                      {event.category && (
                        <Chip size="small" label={`${event.category.icon ?? ''} ${event.category.name}`} />
                      )}
                    </Box>
                    <Typography variant="h6">{event.title}</Typography>
                    {event.description && (
                      <Typography variant="body2" color="text.secondary" sx={{
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        display: '-webkit-box',
                        WebkitLineClamp: 2,
                        WebkitBoxOrient: 'vertical',
                      }}>
                        {event.description}
                      </Typography>
                    )}
                    <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
                      {event.participants?.length ?? 0} участн. · {formatDate(event.createdAt)}
                    </Typography>
                  </CardContent>
                </CardActionArea>
              </Card>
            ))}
          </Box>
          {hasMore && (
            <Box sx={{ display: 'flex', justifyContent: 'center', mt: 4 }}>
              <Button
                variant="outlined"
                size="large"
                onClick={handleLoadMore}
                disabled={loadingMore}
                startIcon={loadingMore ? <CircularProgress size={20} /> : undefined}
              >
                {loadingMore ? 'Загрузка...' : 'Загрузить ещё'}
              </Button>
            </Box>
          )}
        </>
      ) : (
        <Card>
          <CardContent sx={{ textAlign: 'center', py: 6 }}>
            <Typography color="text.secondary">Событий пока нет. Создайте первое!</Typography>
          </CardContent>
        </Card>
      )}

      {/* Диалог создания события */}
      <Dialog open={createOpen} onClose={() => setCreateOpen(false)} maxWidth="sm" fullWidth>
        <form onSubmit={handleCreate}>
          <DialogTitle>Новое событие</DialogTitle>
          <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}>
            <TextField
              label="Название"
              required
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              inputProps={{ maxLength: 200 }}
            />
            <TextField
              label="Описание"
              multiline
              rows={3}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              inputProps={{ maxLength: 2000 }}
            />
            <TextField
              select
              label="Категория"
              value={categoryId}
              onChange={(e) => setCategoryId(e.target.value)}
            >
              <MenuItem value="">Без категории</MenuItem>
              {categories.map((cat) => (
                <MenuItem key={cat.id} value={cat.id}>
                  {cat.icon ? `${cat.icon} ` : ''}{cat.name}
                </MenuItem>
              ))}
            </TextField>
            {formErrors.length > 0 && (
              <Typography color="error" variant="body2">{formErrors.join(' ')}</Typography>
            )}
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setCreateOpen(false)}>Отмена</Button>
            <Button type="submit" variant="contained" disabled={saving}>
              {saving ? 'Создание...' : 'Создать'}
            </Button>
          </DialogActions>
        </form>
      </Dialog>
    </Box>
  );
}
