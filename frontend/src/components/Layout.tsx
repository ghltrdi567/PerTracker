import { AppBar, Avatar, Box, Button, Container, IconButton, Toolbar, Tooltip, Typography } from '@mui/material';
import { Logout as LogoutIcon, Event as EventIcon, Category as CategoryIcon, Settings as SettingsIcon } from '@mui/icons-material';
import { useNavigate, useLocation, Outlet } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

export default function Layout() {
  const { logout, user } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const navItems = [
    { label: 'События', path: '/events', icon: <EventIcon /> },
    { label: 'Категории', path: '/categories', icon: <CategoryIcon /> },
    { label: 'Настройки', path: '/settings', icon: <SettingsIcon /> },
  ];

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      <AppBar position="static">
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 0, mr: 4 }}>
            🎯 PerFunTracker
          </Typography>
          <Box sx={{ flexGrow: 1, display: 'flex', gap: 1 }}>
            {navItems.map((item) => (
              <Button
                key={item.path}
                color="inherit"
                startIcon={item.icon}
                onClick={() => navigate(item.path)}
                sx={{
                  fontWeight: location.pathname.startsWith(item.path) ? 700 : 400,
                  textDecoration: location.pathname.startsWith(item.path) ? 'underline' : 'none',
                }}
              >
                {item.label}
              </Button>
            ))}
          </Box>
          {user && (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mr: 2 }}>
              <Tooltip title={user.email}>
                <Avatar sx={{ width: 32, height: 32, bgcolor: 'secondary.main', fontSize: 14 }}>
                  {user.displayName.charAt(0).toUpperCase()}
                </Avatar>
              </Tooltip>
              <Typography variant="body2" sx={{ display: { xs: 'none', sm: 'block' } }}>
                {user.displayName}
              </Typography>
            </Box>
          )}
          <IconButton color="inherit" onClick={handleLogout} title="Выйти">
            <LogoutIcon />
          </IconButton>
        </Toolbar>
      </AppBar>
      <Container component="main" maxWidth="lg" sx={{ py: 4, flexGrow: 1 }}>
        <Outlet />
      </Container>
    </Box>
  );
}
