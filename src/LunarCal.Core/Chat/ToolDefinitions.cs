namespace LunarCal.Core.Chat;

/// <summary>
/// Defines the schemas and operational limits for the 7 AI assistant tools (FR-CHAT §4.6, SEC-PI-2, FR-CHAT-3).
/// </summary>
public static class ToolDefinitions
{
    // --- Tool Names ---
    public const string CreateEvent = "create_event";
    public const string CreateRecurringEvent = "create_recurring_event";
    public const string CreateLunarRecurringEvent = "create_lunar_recurring_event";
    public const string BulkCreateEvents = "bulk_create_events";
    public const string ListEvents = "list_events";
    public const string UpdateEvent = "update_event";
    public const string DeleteEvent = "delete_event";

    // --- Safety and Operational Limits (FR-CHAT-3, SEC-PI-2) ---
    public const int MaxBulkEvents = 500;
    public const int MaxToolCallsPerSession = 50;
    public const int MaxStringLength = 4000;
    public const int MaxTimespanYears = 5;
    public const int MaxLunarYearsAhead = 20;
    public const int DefaultLunarYearsAhead = 5;

    // --- Tool Definitions with JSON Schemas ---

    public static readonly ToolDefinition CreateEventTool = new(
        CreateEvent,
        "Tạo một sự kiện trong lịch (Google Calendar hoặc Lịch mặc định).",
        """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Tiêu đề của sự kiện (tối đa 4000 ký tự)." },
            "start": { "type": "string", "description": "Thời điểm bắt đầu theo định dạng ISO 8601 (ví dụ 2026-10-16T14:00:00+07:00)." },
            "end": { "type": "string", "description": "Thời điểm kết thúc theo định dạng ISO 8601. Nếu không có, mặc định là 60 phút sau start." },
            "allDay": { "type": "boolean", "description": "True nếu là sự kiện cả ngày, False nếu là sự kiện có giờ cụ thể." },
            "timezone": { "type": "string", "description": "Múi giờ của sự kiện (mặc định theo cài đặt người dùng)." },
            "location": { "type": "string", "description": "Địa điểm hoặc đường link họp (tùy chọn, tối đa 4000 ký tự)." },
            "description": { "type": "string", "description": "Mô tả chi tiết hoặc ghi chú (tùy chọn, tối đa 4000 ký tự)." },
            "reminders": { "type": "array", "items": { "type": "integer" }, "description": "Danh sách số phút nhắc trước khi sự kiện diễn ra (ví dụ [10, 30])." },
            "calendarId": { "type": "string", "description": "Mã lịch đích (mặc định 'primary' hoặc chọn từ Available Calendars)." }
          },
          "required": [ "title", "start" ]
        }
        """);

    public static readonly ToolDefinition CreateRecurringEventTool = new(
        CreateRecurringEvent,
        "Tạo một sự kiện lặp lại theo Dương lịch với quy tắc chuẩn RFC 5545 RRULE (khoảng thời gian tối đa 5 năm).",
        """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Tiêu đề của sự kiện (tối đa 4000 ký tự)." },
            "start": { "type": "string", "description": "Thời điểm bắt đầu của lần diễn ra đầu tiên theo ISO 8601." },
            "end": { "type": "string", "description": "Thời điểm kết thúc của lần diễn ra đầu tiên theo ISO 8601." },
            "rrule": { "type": "string", "description": "Quy tắc lặp RFC 5545 (ví dụ 'RRULE:FREQ=DAILY', 'RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR')." },
            "until": { "type": "string", "description": "Thời điểm kết thúc chuỗi lặp lại theo ISO 8601 (tối đa không quá 5 năm từ start)." },
            "count": { "type": "integer", "description": "Số lần lặp lại tối đa của sự kiện." },
            "allDay": { "type": "boolean", "description": "True nếu là sự kiện cả ngày." },
            "timezone": { "type": "string", "description": "Múi giờ của sự kiện." },
            "location": { "type": "string", "description": "Địa điểm sự kiện." },
            "description": { "type": "string", "description": "Mô tả chi tiết sự kiện." },
            "reminders": { "type": "array", "items": { "type": "integer" }, "description": "Danh sách số phút nhắc trước." },
            "calendarId": { "type": "string", "description": "Mã lịch đích (mặc định 'primary')." }
          },
          "required": [ "title", "start", "rrule" ]
        }
        """);

    public static readonly ToolDefinition CreateLunarRecurringEventTool = new(
        CreateLunarRecurringEvent,
        "Tạo sự kiện lặp lại theo Âm lịch Việt Nam (ngày rằm, mùng 1, giỗ chạp, ngày lễ âm lịch).",
        """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Tiêu đề sự kiện âm lịch (ví dụ 'Rằm tháng Giêng', 'Giỗ cụ')." },
            "lunarDay": { "type": "integer", "description": "Ngày âm lịch trong tháng (1 đến 30)." },
            "lunarMonth": { "type": "integer", "description": "Tháng âm lịch trong năm (1 đến 12). Nếu không chỉ định (null), hiểu là lặp hằng tháng." },
            "time": { "type": "string", "description": "Giờ diễn ra sự kiện theo định dạng HH:mm:ss (tùy chọn)." },
            "yearsAhead": { "type": "integer", "description": "Số năm vật chất hóa luật âm lịch phía trước (từ 1 đến 20, mặc định 5)." },
            "leapPolicy": { "type": "string", "enum": [ "NormalOnly", "IncludeLeap", "LeapOnly" ], "description": "Chính sách tháng nhuận (mặc định NormalOnly)." },
            "reminders": { "type": "array", "items": { "type": "integer" }, "description": "Danh sách số phút nhắc trước." },
            "calendarId": { "type": "string", "description": "Mã lịch đích." }
          },
          "required": [ "title", "lunarDay" ]
        }
        """);

    public static readonly ToolDefinition BulkCreateEventsTool = new(
        BulkCreateEvents,
        "Tạo hàng loạt nhiều sự kiện cùng lúc (tối đa 500 sự kiện).",
        """
        {
          "type": "object",
          "properties": {
            "events": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "start": { "type": "string" },
                  "end": { "type": "string" },
                  "allDay": { "type": "boolean" },
                  "location": { "type": "string" },
                  "description": { "type": "string" },
                  "calendarId": { "type": "string" }
                },
                "required": [ "title", "start" ]
              },
              "description": "Danh sách các sự kiện cần tạo theo lô (tối đa 500 sự kiện)."
            }
          },
          "required": [ "events" ]
        }
        """);

    public static readonly ToolDefinition ListEventsTool = new(
        ListEvents,
        "Tra cứu danh sách các sự kiện trong khoảng thời gian nhất định (tối đa 5 năm).",
        """
        {
          "type": "object",
          "properties": {
            "from": { "type": "string", "description": "Thời điểm bắt đầu khoảng tra cứu theo ISO 8601." },
            "to": { "type": "string", "description": "Thời điểm kết thúc khoảng tra cứu theo ISO 8601 (không quá 5 năm từ from)." },
            "query": { "type": "string", "description": "Từ khóa tìm kiếm trong tiêu đề, mô tả hoặc địa điểm (tùy chọn)." },
            "calendarId": { "type": "string", "description": "Mã lịch cần tra cứu (mặc định tất cả hoặc 'primary')." }
          },
          "required": [ "from", "to" ]
        }
        """);

    public static readonly ToolDefinition UpdateEventTool = new(
        UpdateEvent,
        "Cập nhật các trường thông tin của một sự kiện đã có trong lịch.",
        """
        {
          "type": "object",
          "properties": {
            "eventId": { "type": "string", "description": "Mã định danh duy nhất của sự kiện cần cập nhật." },
            "title": { "type": "string", "description": "Tiêu đề mới của sự kiện (tùy chọn)." },
            "start": { "type": "string", "description": "Thời điểm bắt đầu mới theo ISO 8601 (tùy chọn)." },
            "end": { "type": "string", "description": "Thời điểm kết thúc mới theo ISO 8601 (tùy chọn)." },
            "allDay": { "type": "boolean", "description": "Trạng thái sự kiện cả ngày (tùy chọn)." },
            "timezone": { "type": "string", "description": "Múi giờ mới (tùy chọn)." },
            "location": { "type": "string", "description": "Địa điểm mới (tùy chọn)." },
            "description": { "type": "string", "description": "Mô tả chi tiết mới (tùy chọn)." },
            "reminders": { "type": "array", "items": { "type": "integer" }, "description": "Danh sách nhắc nhở mới." },
            "calendarId": { "type": "string", "description": "Mã lịch chứa sự kiện." }
          },
          "required": [ "eventId" ]
        }
        """);

    public static readonly ToolDefinition DeleteEventTool = new(
        DeleteEvent,
        "Xoá một sự kiện khỏi lịch. Luôn bắt buộc phải có sự xác nhận của người dùng (không xóa hàng loạt).",
        """
        {
          "type": "object",
          "properties": {
            "eventId": { "type": "string", "description": "Mã định danh duy nhất của sự kiện cần xoá." },
            "scope": { "type": "string", "enum": [ "this", "all" ], "description": "Phạm vi xoá: 'this' (chỉ lần diễn ra này) hoặc 'all' (toàn bộ chuỗi lặp)." },
            "calendarId": { "type": "string", "description": "Mã lịch chứa sự kiện cần xoá." }
          },
          "required": [ "eventId", "scope" ]
        }
        """);

    public static readonly IReadOnlyList<ToolDefinition> AllTools =
    [
        CreateEventTool,
        CreateRecurringEventTool,
        CreateLunarRecurringEventTool,
        BulkCreateEventsTool,
        ListEventsTool,
        UpdateEventTool,
        DeleteEventTool,
    ];

    private static readonly Dictionary<string, ToolDefinition> _toolsByName =
        AllTools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds a tool definition by its case-insensitive name.
    /// </summary>
    public static ToolDefinition? FindTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        return _toolsByName.GetValueOrDefault(toolName);
    }

    /// <summary>
    /// Checks whether a tool name is registered in the 7-tool catalog.
    /// </summary>
    public static bool IsKnownTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return _toolsByName.ContainsKey(toolName);
    }
}
