import { Component, type ReactNode } from 'react';

// Per-module error boundary: a failed remote (CDN down, scope clash) shows
// "unavailable" for that module while the rest of the SPA keeps working (§edge case 10).
export class ModuleErrorBoundary extends Component<{ moduleId: string; children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError(): { failed: boolean } {
    return { failed: true };
  }

  render(): ReactNode {
    if (this.state.failed) {
      return <div role="alert">Module unavailable. The rest of the app keeps working.</div>;
    }
    return this.props.children;
  }
}
