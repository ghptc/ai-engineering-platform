export interface Policy {
  maxIterations: number;
  maxToolCalls: number;
  maxDurationMinutes: number;
  maxBudgetUsd: number;
  maxRetries: number;
  requireHumanApprovalForMerge: boolean;
  reviewBeforeCommit: boolean;
}
export interface Project {
  id: string;
  name: string;
  description: string;
  originalContext: string;
  technologyStack: string[];
  status: string;
  version: number;
  createdAt: string;
  updatedAt: string;
  executionPolicy: Policy;
  repositoryConnection?: {
    owner: string;
    repository: string;
    installationId: number;
    defaultBranch: string;
  };
}
export interface Run {
  policy: Policy;
  id: string;
  projectId: string;
  currentStage: string;
  resumeStage: string;
  status: string;
  iteration: number;
  retryCount: number;
  startedAt: string;
  completedAt?: string;
  failureReason?: string;
  cost: number;
  tokenUsage: number;
  toolCalls: number;
  version: number;
}
export interface Entry {
  id: string;
  kind: string;
  projectId: string;
  runId?: string;
  conversationId?: string;
  name: string;
  content: string;
  status: string;
  sequence: number;
  createdAt: string;
  metadata: Record<string, string>;
}
export interface Agent {
  id: string;
  role: string;
  name: string;
  description: string;
  model: string;
  systemInstructions: string;
  enabled: boolean;
  instructionVersion: number;
  allowedTools: string[];
  maxOutputTokens: number;
  inputCostPerMillion: number;
  outputCostPerMillion: number;
}
export interface Page<T> {
  items: T[];
  offset: number;
  limit: number;
}
export interface Settings {
  sandboxConfigured: boolean;
  sandboxReady?: boolean;
  sandboxProvider?: string;
  githubConfigured: boolean;
  openAIConfigured: boolean;
  authenticationMode: string;
  mergeRequiresHumanApproval: boolean;
}
