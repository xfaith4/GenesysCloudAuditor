using System.Windows.Controls;

namespace GenesysExtensionAudit.Views;

public partial class RunAuditView : UserControl
{
    public RunAuditView()
    {
        InitializeComponent();
    }

    private void ProgressConsoleTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        textBox.CaretIndex = textBox.Text.Length;
        textBox.ScrollToEnd();
    }
}
