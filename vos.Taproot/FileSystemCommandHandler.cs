namespace vos.Taproot
{
    public class FileSystemCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _cmd;
        private readonly string _arg;
        private readonly BrokerClient _broker;

        public FileSystemCommandHandler(string cmd, string arg, TextWriter writer, BrokerClient broker)
        {
            _writer = writer;
            _cmd = cmd;
            _arg = arg;
            _broker = broker;
        }

        public async Task ExecuteAsync()
        {
            try
            {
                var tok = (_arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var subcommand = tok.Length > 0 ? tok[0] : "";

                switch (_cmd)
                {
                    case "pwd":
                        _writer.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
                        break;

                    case "cd":
                        ChangeDirectory(subcommand);
                        break;

                    case "serialize":
                        await SerializeModelAsync(subcommand);
                        break;

                    case "deserialize":
                        await DeserializeModelAsync(subcommand);
                        break;
                }
            }
            catch (Exception ex)
            {
                _writer.WriteLine("Error: " + ex.Message);
            }
        }

        private void ChangeDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _writer.WriteLine("Usage: cd <path>");
                return;
            }

            Directory.SetCurrentDirectory(path);
            _writer.WriteLine($"Changed directory to: {Directory.GetCurrentDirectory()}");
        }

        private async Task DeserializeModelAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                _writer.WriteLine("Usage: deserialize <filePath>");
                return;
            }

            // Add .json extension if not provided
            if (!Path.HasExtension(filePath))
            {
                filePath += ".json";
            }

            if (!File.Exists(filePath))
            {
                _writer.WriteLine($"Error: File not found: {filePath}");
                return;
            }

            var modelJson = await File.ReadAllTextAsync(filePath);
            await _broker.SetModelAsync(modelJson);
            _writer.WriteLine($"Model loaded from {filePath}");
        }

        private async Task SerializeModelAsync(string filePath)
        {
            var modelJson = await _broker.GetModelJsonAsync();

            if (string.IsNullOrWhiteSpace(filePath))
            {
                _writer.WriteLine(modelJson);
            }
            else
            {
                // Add .json extension if not provided
                if (!Path.HasExtension(filePath))
                {
                    filePath += ".json";
                }

                await File.WriteAllTextAsync(filePath, modelJson);
                _writer.WriteLine($"Model saved to {filePath}");
            }
        }
    }
}
