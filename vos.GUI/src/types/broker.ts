// Broker service and daemon types — mirrors Broker PascalCase JSON

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
  StartCommand: string;
  StopEndpoint: string;
  HealthEndpoint?: string;
  HealthStatus: string;
  RegisteredAt: string;
  IsRunning: boolean;
  FailureCount: number;
  LastHealthCheckUtc?: string;
  Stats: ServiceStats;
}

export interface DaemonInfo {
  Key: string;
  Port: string;
  IsRunning: boolean;
  IsExternal: boolean;
  ProcessId?: number;
  LastContactTime?: string;
  ConsecutiveFailures: number;
  LastFailureTime?: string;
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
