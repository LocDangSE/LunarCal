using System.Windows.Controls;

namespace LunarCal.App.Controls;

/// <summary>Progress card for the AI commands just sent (P4-R7). Bound to <c>AiProgressViewModel</c>.</summary>
public partial class AiProgressCard : UserControl
{
    /// <summary>Creates the card.</summary>
    public AiProgressCard()
    {
        InitializeComponent();
    }
}
