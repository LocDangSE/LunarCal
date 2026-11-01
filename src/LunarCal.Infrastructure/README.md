# LunarCal.Infrastructure

Adapters for Core abstractions. Depends on Core only (never on the App / WPF). Entry point: `DependencyInjection.AddLunarCalInfrastructure`.

| Folder | Contents | Phase |
| --- | --- | --- |
| `Google/` | OAuth (PKCE, loopback), CalendarGateway, SyncEngine | 3 |
| `Gemini/` | GeminiChatModelClient (REST, IHttpClientFactory), prompts | 4 |
| `Security/` | DpapiSecretStore, LogRedactor, KeyValidator | 4 |
| `Storage/` | SQLCipher store, versioned migrations | 3 |
| `DataLake/` | JsonlWriter, Consent, Export/Delete, uploader | 6 |
