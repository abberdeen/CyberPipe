using System.Runtime.InteropServices;

namespace CyberPipe.Sandbox.Payload;

public static class Program
{
    public static int Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--echo" when i + 1 < args.Length:
                    Console.WriteLine(args[++i]);
                    break;

                case "--err" when i + 1 < args.Length:
                    Console.Error.WriteLine(args[++i]);
                    break;

                case "--hang" when i + 1 < args.Length && int.TryParse(args[++i], out int ms):
                    Thread.Sleep(ms);
                    break;

                case "--alloc-mb" when i + 1 < args.Length && int.TryParse(args[++i], out int mb):
                    AllocateMemory(mb);
                    break;

                case "--check-env" when i + 1 < args.Length:
                    string varName = args[++i];
                    string? val = Environment.GetEnvironmentVariable(varName);
                    Console.WriteLine(val ?? "<NOT_FOUND>");
                    break;

                case "--write-file" when i + 1 < args.Length:
                    string path = args[++i];
                    try
                    {
                        File.WriteAllText(path, "test-data");
                        Console.WriteLine("WRITE_OK");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"WRITE_ERROR: {ex.GetType().Name} - {ex.Message}");
                        return 2;
                    }
                    break;

                case "--exit" when i + 1 < args.Length && int.TryParse(args[++i], out int code):
                    return code;
            }
        }

        return 0;
    }

    private static void AllocateMemory(int mb)
    {
        // Allocate and dirty memory pages so OS actually commits them to working set
        var chunks = new List<byte[]>();
        for (int i = 0; i < mb; i++)
        {
            var chunk = new byte[1024 * 1024];
            Array.Fill(chunk, (byte)1); // Dirty pages
            chunks.Add(chunk);
        }

        // Keep reference alive
        GC.KeepAlive(chunks);
    }
}
