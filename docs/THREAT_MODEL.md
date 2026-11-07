# Threat model

> Skeleton. Expand from spec §5.10 and SEC-CLOUD-8 as features land.

## Assets

Gemini API key, Google OAuth tokens, local calendar cache, AI command queue, data lake outbox, contributor identity.

## Threats and mitigations

| Threat | Mitigation | Status |
| --- | --- | --- |
| API key leaked via logs or files | SEC-KEY-1..3, SEC-KEY-6..11, SEC-LOG-1 | Planned (Phase 4) |
| Malware running as the same user reads tokens | DPAPI only protects against *other* users; fast revoke | Known limitation |
| Prompt injection through calendar content | SEC-PI-* | Planned (Phase 4–5) |
| Unintended bulk create/delete by the model | Mandatory confirmation, limits, undo by batchId | Planned (Phase 5) |
| Network eavesdropping / spoofing | HTTPS only, host allow-list, cert validation never disabled | Planned |
| Data lake exposure | Encrypted raw data, ACL, de-identification, opt-in, export/delete | Planned (Phase 6) |
| Malicious NuGet package | Lock files, NuGet Audit, Dependabot, actions pinned by SHA | In place (Phase 0) |
| Fake update package | Code signing, HTTPS, checksum | Planned (Phase 8) |
| Spoofed client poisoning the cloud dataset | SEC-CLOUD-3 | Planned (Phase 6b) |
| Re-identification from command text | SEC-CLOUD-4 | Planned (Phase 6b) |
| Abuse of the delete-data endpoint | SEC-CLOUD-1/2 | Planned (Phase 6b) |
