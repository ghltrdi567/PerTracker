import { useState } from 'react';
import {
  AppBar,
  Avatar,
  Box,
  Button,
  Container,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Toolbar,
  Tooltip,
  Typography,
} from '@mui/material';
import {
  Logout as LogoutIcon,
  Menu as MenuIcon,
  Event as EventIcon,
  Category as CategoryIcon,
  Settings as SettingsIcon,
  AdminPanelSettings,
} from '@mui/icons-material';
import { useNavigate, useLocation, Outlet } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

const DRAWER_WIDTH = 240;

export default function Layout() {
  const { logout, user } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [drawerOpen, setDrawerOpen] = useState(false);

  const navItems = [
    { label: 'События', path: '/events', icon: <EventIcon /> },
    { label: 'Категории', path: '/categories', icon: <CategoryIcon /> },
    { label: 'Настройки', path: '/settings', icon: <SettingsIcon /> },
    ...(user?.isAdmin
      ? [{ label: 'Админ-панель', path: '/admin', icon: <AdminPanelSettings /> }]
      : []),
  ];

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const isActive = (path: string) => location.pathname.startsWith(path);

  const drawer = (
    <Box sx={{ width: DRAWER_WIDTH }} onClick={() => setDrawerOpen(false)}>
      <Box sx={{ p: 2, display: 'flex', alignItems: 'center', gap: 1 }}>
        <Typography variant="h6">🎯 PerFunTracker</Typography>
      </Box>
      <Divider />
      <List>
        {navItems.map((item) => (
          <ListItem key={item.path} disablePadding>
            <ListItemButton
              selected={isActive(item.path)}
              onClick={() => navigate(item.path)}
            >
              <ListItemIcon>{item.icon}</ListItemIcon>
              <ListItemText primary={item.label} />
            </ListItemButton>
          </ListItem>
        ))}
      </List>
      <Divider />
      <List>
        <ListItem disablePadding>
          <ListItemButton onClick={handleLogout}>
            <ListItemIcon><LogoutIcon /></ListItemIcon>
            <ListItemText primary="Выйти" />
          </ListItemButton>
        </ListItem>
      </List>
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      <AppBar position="static">
        <Toolbar>
          {/* Кнопка меню для мобильных */}
          <IconButton
            color="inherit"
            edge="start"
            onClick={() => setDrawerOpen(true)}
            sx={{ mr: 1, display: { sm: 'none' } }}
          >
            <MenuIcon />
          </IconButton>

          <Typography variant="h6" component="div" sx={{ flexGrow: 0, mr: { xs: 1, md: 4 } }}>
            🎯 PerFunTracker
          </Typography>

          {/* Навигация для десктопа */}
          <Box sx={{ flexGrow: 1, display: { xs: 'none', sm: 'flex' }, gap: 1 }}>
            {navItems.map((item) => (
              <Button
                key={item.path}
                color="inherit"
                startIcon={item.icon}
                onClick={() => navigate(item.path)}
                sx={{
                  fontWeight: isActive(item.path) ? 700 : 400,
                  textDecoration: isActive(item.path) ? 'underline' : 'none',
                  whiteSpace: 'nowrap',
                }}
              >
                {item.label}
              </Button>
            ))}
          </Box>

          {/* Пользователь для десктопа */}
          {user && (
            <Box
              sx={{
                display: { xs: 'none', sm: 'flex' },
                alignItems: 'center',
                gap: 1,
                mr: 2,
              }}
            >
              <Tooltip title={user.email}>
                <Avatar sx={{ width: 32, height: 32, bgcolor: 'secondary.main', fontSize: 14 }}>
                  {user.displayName.charAt(0).toUpperCase()}
                </Avatar>
              </Tooltip>
              <Typography variant="body2" sx={{ display: { xs: 'none', md: 'block' } }}>
                {user.displayName}
                {user.isAdmin && (
                  <Typography component="span" variant="caption" sx={{ ml: 1, color: 'primary.light' }}>
                    [ADMIN]
                  </Typography>
                )}
              </Typography>
            </Box>
          )}

          {/* Кнопка выхода для десктопа */}
          <Box sx={{ display: { xs: 'none', sm: 'block' } }}>
            <IconButton color="inherit" onClick={handleLogout} title="Выйти">
              <LogoutIcon />
            </IconButton>
          </Box>

          {/* Пользователь на мобильных — в правой части */}
          {user && (
            <Box sx={{ display: { xs: 'flex', sm: 'none' }, alignItems: 'center', ml: 'auto', mr: 1 }}>
              <Tooltip title={user.email}>
                <Avatar sx={{ width: 30, height: 30, bgcolor: 'secondary.main', fontSize: 12 }}>
                  {user.displayName.charAt(0).toUpperCase()}
                </Avatar>
              </Tooltip>
            </Box>
          )}
          {user && (
            <IconButton
              color="inherit"
              onClick={handleLogout}
              title="Выйти"
              sx={{ display: { xs: 'inline-flex', sm: 'none' }, p: 1 }}
            >
              <LogoutIcon />
            </IconButton>
          )}
        </Toolbar>
      </AppBar>

      {/* Drawer (мобильное меню) */}
      <Drawer
        anchor="left"
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        sx={{ display: { sm: 'none' } }}
      >
        {drawer}
      </Drawer>

      <Container component="main" maxWidth="lg" sx={{ py: 4, flexGrow: 1 }}>
        <Outlet />
      </Container>
    </Box>
  );
}
