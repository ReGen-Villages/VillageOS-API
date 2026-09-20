using System.Text.Json;

namespace vos.Taproot;

/// <summary>Accounts, administered through the same route the platform's Accounts page uses, so the
/// command line and the console say the same things and refuse the same things. Accounts and models
/// are named, not identified: a model's name is the rest of the line, spaces and all, because that is
/// how a person knows it. A password is prompted for rather than taken from the line, where it would
/// stay in the shell's history.</summary>
public class UserCommandHandler
{
    private readonly TextWriter _writer;
    private readonly TextReader _reader;
    private readonly string _arg;
    private readonly MyceliumClient _mycelium;

    public UserCommandHandler(string arg, TextReader reader, TextWriter writer, MyceliumClient mycelium)
    {
        _arg = arg;
        _reader = reader;
        _writer = writer;
        _mycelium = mycelium;
    }

    public async Task ExecuteAsync()
    {
        try
        {
            var tok = (_arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) { ShowUsage(); return; }

            switch (tok[0])
            {
                case "list":
                    await ListAsync();
                    break;
                case "create" when tok.Length >= 3:
                    await CreateAsync(tok[1], tok[2], RestOfTheLine(tok, 3));
                    break;
                case "create":
                    _writer.WriteLine("Usage: user create <username> <role> [<model name>]");
                    break;
                case "grant" or "revoke" when tok.Length >= 3:
                    await SayAsync(new { view = tok[0], record = tok[1], model = RestOfTheLine(tok, 2) });
                    break;
                case "grant" or "revoke":
                    _writer.WriteLine($"Usage: user {tok[0]} <username> <model name>");
                    break;
                case "role" when tok.Length >= 3:
                    await SayAsync(new { view = "change-role", record = tok[1], role = tok[2] });
                    break;
                case "role":
                    _writer.WriteLine("Usage: user role <username> <admin|editor|viewer>");
                    break;
                case "reset-password" when tok.Length >= 2:
                    await SayAsync(new { view = "reset-password", record = tok[1], password = Prompt("New password: ") });
                    break;
                case "reset-password":
                    _writer.WriteLine("Usage: user reset-password <username>");
                    break;
                case "delete" when tok.Length >= 2:
                    await SayAsync(new { view = "delete", record = tok[1] });
                    break;
                case "delete":
                    _writer.WriteLine("Usage: user delete <username>");
                    break;
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
            _writer.WriteLine("Error: " + OperatorMessage.For(ex));
        }
    }

    private async Task ListAsync()
    {
        var accounts = await _mycelium.AdministerAccountsAsync(new { view = "accounts" });
        var rows = accounts.EnumerateArray().ToList();
        if (rows.Count == 0)
        {
            _writer.WriteLine("No accounts.");
            return;
        }

        _writer.WriteLine($"{"Account",-20} {"Role",-8} {"May enter",-40} {"Password",-28} Created");
        foreach (var row in rows)
        {
            _writer.WriteLine(
                $"{Text(row, "name"),-20} {Text(row, "role"),-8} {Text(row, "models"),-40} {Text(row, "password"),-28} {Text(row, "created")}");
        }
    }

    private async Task CreateAsync(string username, string role, string? modelName)
    {
        var password = Prompt("First password: ");
        await SayAsync(modelName is null
            ? new { view = "create", username, password, role }
            : new { view = "create", username, password, role, models = new[] { modelName } });
    }

    /// <summary>Posts one act and writes what the route said on taking it.</summary>
    private async Task SayAsync(object act)
    {
        var answer = await _mycelium.AdministerAccountsAsync(act);
        _writer.WriteLine(Text(answer, "said"));
    }

    private string Prompt(string question)
    {
        _writer.Write(question);
        return _reader.ReadLine() ?? "";
    }

    private static string? RestOfTheLine(string[] tok, int from) =>
        tok.Length > from ? string.Join(" ", tok.Skip(from)) : null;

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ToString() : "";

    private async Task ChangePasswordAsync(string userId)
    {
        if (!Guid.TryParse(userId, out var id))
        {
            _writer.WriteLine("Usage: user change-password <user-guid>");
            return;
        }

        var currentPassword = Prompt("Current password: ");
        var newPassword = Prompt("New password: ");

        await _mycelium.ChangePasswordAsync(id, currentPassword, newPassword);
        _writer.WriteLine("Password changed.");
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage:");
        _writer.WriteLine("  user list                                  - Every account, its role and the models it may enter");
        _writer.WriteLine("  user create <username> <role> [<model name>] - Add an account; prompts for its first password");
        _writer.WriteLine("  user grant <username> <model name>         - Let an account enter a model");
        _writer.WriteLine("  user revoke <username> <model name>        - Take a model off an account");
        _writer.WriteLine("  user role <username> <admin|editor|viewer> - Change an account's role");
        _writer.WriteLine("  user reset-password <username>             - Set a password the person must then change");
        _writer.WriteLine("  user delete <username>                     - Remove an account");
        _writer.WriteLine("  user change-password <user-guid>           - Change your own password");
    }
}
