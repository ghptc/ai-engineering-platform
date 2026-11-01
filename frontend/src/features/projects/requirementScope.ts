export interface ScopedRequirement {
  id: string;
  description: string;
  acceptanceCriteria: string[];
  priority: number | null;
  classification: string;
  dependencies: string[];
  complexity: string;
  implementationState: string;
  components: string[];
  milestone: string | null;
  roles: string[];
  verificationStrategy: string;
  sources: {
    documentSha256: string;
    location: string;
    originalLabel: string | null;
    text: string;
  }[];
  warnings: string[];
  ownerReviewed: boolean;
  reviewNote: string;
}
export interface RequirementScope {
  projectId: string;
  revision: string;
  parentRevision: string | null;
  version: number;
  createdAt: string;
  reason: string;
  readyForEstimation: boolean;
  warnings: string[];
  document: {
    key: string;
    fileName: string;
    mediaType: string;
    sha256: string;
    extractedText: string;
  };
  requirements: ScopedRequirement[];
}
export interface ScopeHistory {
  items: {
    revision: string;
    version: number;
    reason: string;
    fileName: string;
    count: number;
  }[];
  nextBefore: string | null;
}
