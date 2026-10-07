import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import MembersPage from './MembersPage';

// Dev entry only: production is consumed as a Federation remote (remoteEntry.js).
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MembersPage />
  </StrictMode>,
);
