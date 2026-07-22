namespace CodexBalanceTray;

internal sealed class LoginProgressForm : Form
{
    private readonly Action _reopenBrowser;
    private bool _completed;
    private bool _cancelRaised;

    public LoginProgressForm(Action reopenBrowser)
    {
        _reopenBrowser = reopenBrowser;

        Text = "连接 ChatGPT";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(430, 145);

        var message = new Label
        {
            AutoSize = false,
            Text = "官方登录页面已经在浏览器中打开。\r\n完成登录后，此窗口会自动关闭。",
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(24, 20),
            Size = new Size(380, 58)
        };

        var reopenButton = new Button
        {
            Text = "重新打开浏览器",
            AutoSize = true,
            Location = new Point(192, 92)
        };
        reopenButton.Click += (_, _) => _reopenBrowser();

        var cancelButton = new Button
        {
            Text = "取消",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Location = new Point(333, 92)
        };
        cancelButton.Click += (_, _) => RaiseCancel();

        Controls.Add(message);
        Controls.Add(reopenButton);
        Controls.Add(cancelButton);
        CancelButton = cancelButton;
        FormClosing += OnFormClosing;
    }

    public event Action? CancelRequested;

    public void CompleteAndClose()
    {
        _completed = true;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_completed)
        {
            RaiseCancel();
        }
    }

    private void RaiseCancel()
    {
        if (_cancelRaised)
        {
            return;
        }

        _cancelRaised = true;
        CancelRequested?.Invoke();
    }
}
