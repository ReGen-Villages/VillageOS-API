// The broker's JSON, with its capitalised names. A service carries its daemon's live state inline.

interface ServiceStatistics {
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
  Stats: ServiceStatistics;
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

interface EndpointServiceStatistics {
  RequestCount: number;
  TotalResponseMs: number;
  AverageResponseMs: number;
  ErrorCount: number;
  LastRequestUtc?: string;
}

export interface EndpointServiceInformation {
  ObjectId: string;
  Name: string;
  Subdomain: string;
  ServicePort: string;
  Stats: EndpointServiceStatistics;
}
