import { Component, type ReactNode } from 'react';
import { Alert, AlertDescription, AlertTitle } from './ui/alert';

// Per-module error boundary: a failed remote (CDN down, scope clash) shows
// "unavailable" for that module while the rest of the SPA keeps working (§edge case 10).
export class ModuleErrorBoundary extends Component<{ moduleId: string; children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError(): { failed: boolean } {
    return { failed: true };
  }

  render(): ReactNode {
    if (this.state.failed) {
      return (
        <Alert variant="destructive">
          <AlertTitle>Module unavailable</AlertTitle>
          <AlertDescription>
            “{this.props.moduleId}” failed to load. The rest of the portal keeps working — try reloading the page.
          </AlertDescription>
        </Alert>
      );
    }
    return this.props.children;
  }
}
