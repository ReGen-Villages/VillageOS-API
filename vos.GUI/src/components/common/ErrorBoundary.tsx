import { Component, type ReactNode, type ErrorInfo } from 'react';

interface Props {
  children: ReactNode;
  fallback?: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
  errorInfo: ErrorInfo | null;
}

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false, error: null, errorInfo: null };
  }

  static getDerivedStateFromError(error: Error): Partial<State> {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('[ErrorBoundary] Caught error:', error, errorInfo);
    this.setState({ errorInfo });
  }

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) return this.props.fallback;
      return (
        <div className="p-6 m-4 bg-red-950 border border-red-800 rounded-lg text-red-200 overflow-auto max-h-[80vh]">
          <h2 className="text-lg font-bold text-red-400 mb-2">Something went wrong</h2>
          <p className="mb-4 font-mono text-sm text-red-300">{this.state.error?.message}</p>
          <details className="text-xs">
            <summary className="cursor-pointer text-red-400 hover:text-red-300 mb-2">Stack trace</summary>
            <pre className="whitespace-pre-wrap text-red-400/80">
              {this.state.error?.stack}
            </pre>
            {this.state.errorInfo && (
              <pre className="whitespace-pre-wrap text-red-400/60 mt-2">
                {this.state.errorInfo.componentStack}
              </pre>
            )}
          </details>
          <button
            onClick={() => this.setState({ hasError: false, error: null, errorInfo: null })}
            className="mt-4 px-3 py-1.5 bg-red-800 hover:bg-red-700 rounded text-sm text-red-200 transition-colors"
          >
            Try again
          </button>
        </div>
      );
    }

    return this.props.children;
  }
}
