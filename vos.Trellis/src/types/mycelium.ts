// Mycelium service types — mirrors Mycelium PascalCase JSON.
// A service carries its supervised daemon's live state inline (IsRunning/ProcessId/LastContactTime).

export interface ServiceStats {
  RequestsForwarded: number;
  TotalResponseMilliseconds: number;
  AverageResponseMilliseconds: number;
  LastRequestUtc?: string;
}

export interface RegisteredService {
  HandlerId: string;
  ServiceName: string;
  EndpointUrl: string;
  HealthEndpoint?: string;
  HealthStatus: string;
  RegisteredAt: string;
  IsRunning: boolean;
  FailureCount: number;
  LastHealthCheckUtc?: string;
  Stats: ServiceStats;
  ProcessId?: number;
  IsExternal: boolean;
  LastContactTime?: string;
  ConsecutiveFailures: number;
}

export interface ActivityEvent {
  Type: string;
  Timestamp: string;
  Description: string;
  Details?: unknown;
}

export type HealthStatus = 'Healthy' | 'Unhealthy' | 'Unreachable' | 'Unknown';

export interface EndpointServiceStats {
  RequestCount: number;
  TotalResponseMs: number;
  AverageResponseMs: number;
  ErrorCount: number;
  LastRequestUtc?: string;
}

export interface EndpointServiceInfo {
  ThingId: string;
  Name: string;
  Subdomain: string;
  ServicePort: string;
  Stats: EndpointServiceStats;
}
