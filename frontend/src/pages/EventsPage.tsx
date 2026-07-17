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
  Pagination,
  TextField,
  Typography,
} from '@mui/material';
import { Add as AddIcon } from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import { getEvents, createEvent } from '../api/events';
import { getCategories } from '../api/categories';
import type { Category, EventResponse, PagedResult } from '../types/api';
import { getApiErrors, formatDate } from '../utils/helpers';
import { useToast } from '../components/ToastProvider';

export default function EventsPage() {
  const navigate = useNavigate();
  const toast = useToast();
  const [data, setData] = useState<PagedResult<EventResponse> | null>(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);

  // Поля формы создания
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [formErrors, setFormErrors] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  const loadEvents = async () => {
    setLoading(true);
    try {
      const res = await getEvents(page);
      setData(res);
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadEvents();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page]);

  const openCreate = async () => {
    setCreateOpen(true);
    try {
      const res = await getCategories();
      setCategories(res.items);
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
      ) : data && data.items.length > 0 ? (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', md: '1fr 1fr 1fr' }, gap: 2 }}>
            {data.items.map((event) => (
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
                      {event.participants.length} участн. · {formatDate(event.createdAt)}
                    </Typography>
                  </CardContent>
                </CardActionArea>
              </Card>
            ))}
          </Box>
          {data.totalPages > 1 && (
            <Box sx={{ display: 'flex', justifyContent: 'center', mt: 4 }}>
              <Pagination
                count={data.totalPages}
                page={page}
                onChange={(_, p) => setPage(p)}
                color="primary"
              />
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
