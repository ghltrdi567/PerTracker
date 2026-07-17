import { useEffect, useState, type FormEvent } from 'react';
import {
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  IconButton,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  TextField,
  Typography,
} from '@mui/material';
import {
  ArrowBack as ArrowBackIcon,
  Delete as DeleteIcon,
  Edit as EditIcon,
  CheckCircle as CheckCircleIcon,
  PersonAdd as PersonAddIcon,
} from '@mui/icons-material';
import { useNavigate, useParams } from 'react-router-dom';
import {
  getEvent,
  updateEvent,
  deleteEvent,
  addParticipant,
  removeParticipant,
  completeEvent,
  getCompletions,
} from '../api/events';
import { getCategories } from '../api/categories';
import type { Category, Completion, EventResponse, Participant } from '../types/api';
import { ParticipantRole } from '../types/api';
import { getApiErrors, formatDate } from '../utils/helpers';
import { useToast } from '../components/ToastProvider';
import { useAuth } from '../context/AuthContext';

export default function EventDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();
  const { user } = useAuth();

  const [event, setEvent] = useState<EventResponse | null>(null);
  const [loading, setLoading] = useState(true);

  // Завершения
  const [completions, setCompletions] = useState<Completion[]>([]);
  const [compPage, setCompPage] = useState(1);
  const [compHasMore, setCompHasMore] = useState(false);
  const [compLoading, setCompLoading] = useState(false);
  const [compNote, setCompNote] = useState('');
  const [completing, setCompleting] = useState(false);
  const [compRefreshKey, setCompRefreshKey] = useState(0);

  // Редактирование
  const [editOpen, setEditOpen] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);
  const [editTitle, setEditTitle] = useState('');
  const [editDescription, setEditDescription] = useState('');
  const [editCategoryId, setEditCategoryId] = useState('');
  const [editErrors, setEditErrors] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  // Добавление участника
  const [addEmail, setAddEmail] = useState('');
  const [adding, setAdding] = useState(false);

  const loadEvent = async () => {
    if (!id) return;
    setLoading(true);
    try {
      const res = await getEvent(id);
      setEvent(res);
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
      navigate('/events');
    } finally {
      setLoading(false);
    }
  };

  const loadCompletions = async (pageNum: number, append: boolean) => {
    if (!id) return;
    if (append) setCompLoading(true);
    try {
      const res = await getCompletions(id, pageNum);
      const newItems = res?.items ?? [];
      setCompletions(append ? (prev) => [...prev, ...newItems] : newItems);
      setCompHasMore((res?.page ?? 1) < (res?.totalPages ?? 1));
      setCompPage(pageNum);
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
      if (!append) setCompletions([]);
    } finally {
      setCompLoading(false);
    }
  };

  const handleLoadMoreCompletions = () => loadCompletions(compPage + 1, true);
  void handleLoadMoreCompletions;

  useEffect(() => {
    loadEvent();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  useEffect(() => {
    loadCompletions(1, false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, compRefreshKey]);

  if (loading || !event) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  // Проверяем, является ли текущий пользователь владельцем события
  const isOwner = user?.userId === event.ownerUserId;

  const openEdit = async () => {
    setEditTitle(event.title);
    setEditDescription(event.description ?? '');
    setEditCategoryId(event.category?.id ?? '');
    setEditOpen(true);
    try {
      const res = await getCategories();
      setCategories(res?.items ?? []);
    } catch {
      // ignore
    }
  };

  const handleEdit = async (e: FormEvent) => {
    e.preventDefault();
    if (!id) return;
    setEditErrors([]);
    setSaving(true);
    try {
      const updated = await updateEvent(id, {
        title: editTitle,
        description: editDescription || null,
        categoryId: editCategoryId || null,
      });
      setEvent(updated);
      toast.show('Событие обновлено');
      setEditOpen(false);
    } catch (err) {
      setEditErrors(getApiErrors(err));
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    if (!id) return;
    if (!confirm('Удалить событие? Это действие необратимо.')) return;
    try {
      await deleteEvent(id);
      toast.show('Событие удалено');
      navigate('/events');
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    }
  };

  const handleAddParticipant = async (e: FormEvent) => {
    e.preventDefault();
    if (!id) return;
    setAdding(true);
    try {
      await addParticipant(id, addEmail);
      toast.show('Участник добавлен');
      setAddEmail('');
      await loadEvent();
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    } finally {
      setAdding(false);
    }
  };

  const handleRemoveParticipant = async (participant: Participant) => {
    if (!id) return;
    const isSelf = participant.userId === user?.userId;
    const msg = isSelf
      ? 'Выйти из события?'
      : `Удалить участника ${participant.displayName}?`;
    if (!confirm(msg)) return;
    try {
      await removeParticipant(id, participant.userId);
      toast.show(isSelf ? 'Вы вышли из события' : 'Участник удалён');
      if (isSelf) {
        navigate('/events');
      } else {
        await loadEvent();
      }
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    }
  };

  const handleComplete = async (e: FormEvent) => {
    e.preventDefault();
    if (!id) return;
    setCompleting(true);
    try {
      await completeEvent(id, { note: compNote || null });
      toast.show('Завершение отмечено!');
      setCompNote('');
      setCompRefreshKey((k) => k + 1);
    } catch (err) {
      toast.show(getApiErrors(err).join(' '), 'error');
    } finally {
      setCompleting(false);
    }
  };

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
        <IconButton onClick={() => navigate('/events')}>
          <ArrowBackIcon />
        </IconButton>
        {isOwner && (
          <>
            <IconButton onClick={openEdit} color="primary"><EditIcon /></IconButton>
            <IconButton onClick={handleDelete} color="error"><DeleteIcon /></IconButton>
          </>
        )}
      </Box>

      <Typography variant="h4" gutterBottom>{event.title}</Typography>
      {event.category && (
        <Chip label={`${event.category.icon ?? ''} ${event.category.name}`} sx={{ mb: 2 }} />
      )}
      {event.description && (
        <Typography variant="body1" color="text.secondary" sx={{ mb: 3 }}>
          {event.description}
        </Typography>
      )}
      <Typography variant="caption" color="text.secondary">
        Создано: {formatDate(event.createdAt)}
      </Typography>

      <Divider sx={{ my: 3 }} />

      {/* Участники */}
      <Typography variant="h6" gutterBottom>Участники</Typography>
      {isOwner && (
        <Box component="form" onSubmit={handleAddParticipant} sx={{ display: 'flex', gap: 1, mb: 2 }}>
          <TextField
            size="small"
            label="Email участника"
            type="email"
            required
            value={addEmail}
            onChange={(e) => setAddEmail(e.target.value)}
            sx={{ flexGrow: 1 }}
          />
          <Button type="submit" variant="contained" startIcon={<PersonAddIcon />} disabled={adding}>
            {adding ? '...' : 'Добавить'}
          </Button>
        </Box>
      )}
      <List>
        {(event.participants ?? []).map((p) => (
          <ListItem
            key={p.userId}
            secondaryAction={
              <IconButton
                edge="end"
                onClick={() => handleRemoveParticipant(p)}
                color="error"
                disabled={
                  // Владелец не может удалить сам себя
                  (isOwner && p.userId === user?.userId) ||
                  // Не-владелец может удалить только себя
                  (!isOwner && p.userId !== user?.userId)
                }
                title={p.userId === user?.userId ? 'Выйти' : 'Удалить'}
              >
                <DeleteIcon />
              </IconButton>
            }
          >
            <ListItemText
              primary={
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  {p.displayName}
                  <Chip
                    size="small"
                    label={p.role === ParticipantRole.Owner ? 'Владелец' : 'Участник'}
                    color={p.role === ParticipantRole.Owner ? 'primary' : 'default'}
                    variant={p.role === ParticipantRole.Owner ? 'filled' : 'outlined'}
                  />
                </Box>
              }
              secondary={p.email}
            />
          </ListItem>
        ))}
      </List>

      <Divider sx={{ my: 3 }} />

      {/* Отметить завершение */}
      <Typography variant="h6" gutterBottom>Отметить завершение</Typography>
      <Box component="form" onSubmit={handleComplete} sx={{ display: 'flex', gap: 1, mb: 3 }}>
        <TextField
          size="small"
          label="Заметка (необязательно)"
          value={compNote}
          onChange={(e) => setCompNote(e.target.value)}
          inputProps={{ maxLength: 1000 }}
          sx={{ flexGrow: 1 }}
        />
        <Button type="submit" variant="contained" startIcon={<CheckCircleIcon />} disabled={completing}>
          {completing ? '...' : 'Отметить'}
        </Button>
      </Box>

      {/* История завершений — в виде чата */}
      <Typography variant="h6" gutterBottom>История завершений</Typography>
      {completions.length > 0 ? (
        <Box sx={{ mb: 2 }}>
          {completions.map((c) => {
            const isOwn = c.completedByUserId === user?.userId;
            return (
              <Box
                key={c.id}
                sx={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: isOwn ? 'flex-end' : 'flex-start',
                  mb: 1.5,
                }}
              >
                {/* Имя и время */}
                <Box
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 0.5,
                    flexDirection: isOwn ? 'row-reverse' : 'row',
                    mb: 0.3,
                  }}
                >
                  <Typography variant="caption" fontWeight={600}>
                    {isOwn ? 'Вы' : c.completedByName}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    {formatDate(c.completedAt)}
                  </Typography>
                </Box>
                {/* Пузырёк сообщения */}
                <Box
                  sx={{
                    maxWidth: '75%',
                    px: 2,
                    py: 1,
                    borderRadius: isOwn ? '18px 18px 4px 18px' : '18px 18px 18px 4px',
                    bgcolor: isOwn ? 'primary.main' : 'grey.200',
                    color: isOwn ? 'primary.contrastText' : 'text.primary',
                    wordBreak: 'break-word',
                  }}
                >
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, flexWrap: 'wrap' }}>
                    <Typography variant="body2">✅ Отметил(а) завершение</Typography>
                  </Box>
                  {c.note && (
                    <Typography variant="body2" sx={{ mt: 0.5, opacity: 0.9 }}>
                      📝 {c.note}
                    </Typography>
                  )}
                </Box>
              </Box>
            );
          })}
          {compHasMore && (
            <Box sx={{ display: 'flex', justifyContent: 'center', mt: 2 }}>
              <Button
                variant="outlined"
                onClick={() => loadCompletions(compPage + 1, true)}
                disabled={compLoading}
                startIcon={compLoading ? <CircularProgress size={20} /> : undefined}
              >
                {compLoading ? 'Загрузка...' : 'Загрузить ещё'}
              </Button>
            </Box>
          )}
        </Box>
      ) : (
        <Typography color="text.secondary" sx={{ mb: 2 }}>Завершений пока нет.</Typography>
      )}

      {/* Диалог редактирования */}
      <Dialog open={editOpen} onClose={() => setEditOpen(false)} maxWidth="sm" fullWidth>
        <form onSubmit={handleEdit}>
          <DialogTitle>Редактировать событие</DialogTitle>
          <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}>
            <TextField
              label="Название"
              required
              value={editTitle}
              onChange={(e) => setEditTitle(e.target.value)}
              inputProps={{ maxLength: 200 }}
            />
            <TextField
              label="Описание"
              multiline
              rows={3}
              value={editDescription}
              onChange={(e) => setEditDescription(e.target.value)}
              inputProps={{ maxLength: 2000 }}
            />
            <TextField
              select
              label="Категория"
              value={editCategoryId}
              onChange={(e) => setEditCategoryId(e.target.value)}
            >
              <MenuItem value="">Без категории</MenuItem>
              {categories.map((cat) => (
                <MenuItem key={cat.id} value={cat.id}>
                  {cat.icon ? `${cat.icon} ` : ''}{cat.name}
                </MenuItem>
              ))}
            </TextField>
            {editErrors.length > 0 && (
              <Typography color="error" variant="body2">{editErrors.join(' ')}</Typography>
            )}
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setEditOpen(false)}>Отмена</Button>
            <Button type="submit" variant="contained" disabled={saving}>
              {saving ? 'Сохранение...' : 'Сохранить'}
            </Button>
          </DialogActions>
        </form>
      </Dialog>
    </Box>
  );
}
