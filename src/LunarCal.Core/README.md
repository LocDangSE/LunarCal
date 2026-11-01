# LunarCal.Core

Pure domain layer. References **only the BCL** (enforced by `ArchitectureTests`). No UI, IO, HTTP or Windows APIs.

| Folder | Contents | Phase |
| --- | --- | --- |
| `Lunar/` | LunarCalendar, LunarDate, LunarRule, CanChi, TietKhi (FR-LUNAR) | 1, 5 |
| `Calendar/` | CalendarEvent, CalendarInfo, RecurrenceHelpers | 3 |
| `Chat/` | ChatTurn, ToolCall, ToolDefinitions, ChatOrchestrator (FR-CHAT, FR-CONFIRM, FR-QUEUE) | 4–5 |
| `Abstractions/` | ICalendarGateway, IChatModelClient, ISecretStore, IEventStore, IDataCollector, IClock, IContributionUploader | 1+ |
