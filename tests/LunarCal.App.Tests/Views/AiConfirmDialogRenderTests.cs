using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeAssertions;
using LunarCal.App.Localization;
using LunarCal.App.ViewModels;
using LunarCal.App.Views;
using LunarCal.Core.Chat;
using Xunit;

namespace LunarCal.App.Tests.Views;

/// <summary>
/// FR-CONFIRM-1 (Figma 02 <c>2:2</c>): the confirmation dialog must load with the app resources and show a proposal
/// shaped by the real tool schema. Set <c>LUNARCAL_RENDER_DIR</c> to also write a PNG for side-by-side review.
/// </summary>
[Collection(nameof(EventEditorDialogTests))]
public sealed class AiConfirmDialogRenderTests
{
    private static readonly DateTimeOffset _typedAt = new(2026, 10, 16, 8, 41, 0, TimeSpan.FromHours(7));

    [Fact]
    public void FR_CONFIRM_1_Dialog_loads_and_shows_a_model_shaped_proposal()
    {
        string? title = null;
        string? status = null;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            var previousCulture = LocalizationService.Instance.CurrentCulture;
            try
            {
                // The XAML resolves strings through the shared service; use Vietnamese like the running app.
                LocalizationService.Instance.SetCulture(SupportedCultures.Vietnamese);
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Resources/Tokens.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Themes/Light.xaml", UriKind.Relative) });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/LunarCal;component/Resources/Controls.xaml", UriKind.Relative) });

                var vm = new AiConfirmDialogViewModel(new LocalizationService(SupportedCultures.Vietnamese));
                var command = new AiCommand
                {
                    Id = "cmd-1",
                    DisplayNo = 1000,
                    Text = "Nhắc tôi họp nhóm thứ 5 tuần sau lúc 9h",
                    TypedAt = _typedAt,
                    TimeZone = "SE Asia Standard Time",
                    InputLang = "vi",
                    Status = AiCommandStatus.NeedsConfirmation,
                    CreatedAt = _typedAt,
                    UpdatedAt = _typedAt,
                };
                var call = new ToolCall(
                    ToolDefinitions.CreateEvent,
                    """{"title": "Họp nhóm", "start": "2026-10-22T09:00:00+07:00", "end": "2026-10-22T10:00:00+07:00", "reminders": [15]}""");
                vm.Initialize(command, call, new AiConfidenceResult(96, AiConfidenceLevel.High, []), AiBusinessValidationResult.Success(), isLunarEnabled: true, isTrainingOptIn: false);

                var dialog = new AiConfirmDialog(vm);
                var content = (FrameworkElement)dialog.Content;
                content.Measure(new Size(564, 900));
                content.Arrange(new Rect(new Size(564, 900)));
                content.UpdateLayout();

                title = vm.EventTitle;
                status = vm.ValidationSeverity.ToString();

                var dir = Environment.GetEnvironmentVariable("LUNARCAL_RENDER_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    var size = new Size(Math.Ceiling(content.ActualWidth), Math.Ceiling(content.ActualHeight));
                    var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(dir, "ai-confirm-dialog.png"));
                    encoder.Save(stream);
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                LocalizationService.Instance.SetCulture(previousCulture);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        error.Should().BeNull(error?.ToString());
        title.Should().Be("Họp nhóm");
        status.Should().Be(nameof(AiBusinessValidationSeverity.Pass));
    }
}
