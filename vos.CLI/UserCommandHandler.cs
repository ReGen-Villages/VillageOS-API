namespace vos.CLI;

public class UserCommandHandler
{
    private readonly TextWriter _writer;
    private readonly TextReader _reader;
    private readonly string _arg;
    private readonly BrokerClient _broker;

    public UserCommandHandler(string arg, TextReader reader, TextWriter writer, BrokerClient broker)
    {
        _arg = arg;
        _reader = reader;
        _writer = writer;
        _broker = broker;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            var tok = (_arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) { ShowUsage(); return; }

            switch (tok[0])
            {
                case "change-password" when tok.Length >= 2:
                    await ChangePasswordAsync(tok[1]);
                    break;
                default:
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            _writer.WriteLine("Error: " + ex.Message);
        }
    }

    private async Task ChangePasswordAsync(string userId)
    {
        if (!Guid.TryParse(userId, out var id))
        {
            _writer.WriteLine("Usage: user change-password <user-guid>");
            return;
        }

        _writer.Write("Current password: ");
        var currentPassword = _reader.ReadLine() ?? "";

        _writer.Write("New password: ");
        var newPassword = _reader.ReadLine() ?? "";

        await _broker.ChangePasswordAsync(id, currentPassword, newPassword);
        _writer.WriteLine("Password changed.");
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage:");
        _writer.WriteLine("  user change-password <user-guid>  - Change a user's password");
    }
}
